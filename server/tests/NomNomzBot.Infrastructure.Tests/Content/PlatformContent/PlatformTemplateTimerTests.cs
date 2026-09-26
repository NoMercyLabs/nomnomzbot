// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// The <c>timer</c> platform-template kind, end to end: author and publish, then install into a channel
/// through the real <see cref="TimerManagementService"/> (timer limit, variation limit, helper check,
/// pipeline ownership). Install always adds a timer; it never replaces one the channel made.
/// </summary>
public sealed class PlatformTemplateTimerTests : IAsyncDisposable
{
    private const string HydrateTemplate =
        """{"name":"Hydrate","messages":["Drink some water, {channel}!","Stretch your legs."],"intervalMinutes":45,"minChatActivity":5,"fireOnce":false,"isEnabled":true}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly IResourceQuotaService _quota = Substitute.For<IResourceQuotaService>();
    private readonly TimerTemplateInstaller _installer;

    public PlatformTemplateTimerTests()
    {
        AllowQuota(true);
        TimerManagementService timers = new(
            _h.Db,
            Substitute.For<IEventBus>(),
            _quota,
            new TemplateHelperValidator()
        );
        _installer = new(_h.Db, timers, new TemplateHelperValidator());
    }

    private void AllowQuota(bool allowed)
    {
        _quota
            .GetCurrentCountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(0L));
        _quota
            .CheckAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(
                    new QuotaCheckDto(allowed, call.ArgAt<string>(1), 0, 10, allowed ? 10 : 0)
                )
            );
    }

    [Fact]
    public async Task Install_adds_a_timer_with_the_template_fields_and_provenance()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        DomainTimer row = await _h.Db.Timers.SingleAsync();
        row.Id.Should().Be(result.Value.EntityId);
        result.Value.Kind.Should().Be(PlatformContentKinds.Timer);
        result.Value.Name.Should().Be("Hydrate");
        row.BroadcasterId.Should().Be(channel.Id);
        row.Name.Should().Be("Hydrate");
        row.Messages.Should().Equal("Drink some water, {channel}!", "Stretch your legs.");
        row.IntervalMinutes.Should().Be(45);
        row.MinChatActivity.Should().Be(5);
        row.FireOnce.Should().BeFalse();
        row.IsEnabled.Should().BeTrue();
        row.PipelineId.Should().BeNull();
        row.PlatformSourceDefinitionId.Should().Be(definitionId);
        row.PlatformSourceVersion.Should().Be(1);
        row.PlatformSourceHash.Should().Be(TimerTemplatePayload.FromEntity(row).ComputeHash());
        row.PlatformSourceSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Install_never_replaces_a_timer_the_channel_made_and_never_touches_another_channel()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        Guid ownOfA = Guid.CreateVersion7();
        Guid ownOfB = Guid.CreateVersion7();
        _h.Db.Timers.AddRange(
            new DomainTimer
            {
                Id = ownOfA,
                BroadcasterId = channelA.Id,
                Name = "Hydrate",
                Messages = ["A says drink"],
            },
            new DomainTimer
            {
                Id = ownOfB,
                BroadcasterId = channelB.Id,
                Name = "Hydrate",
                Messages = ["B says drink"],
            }
        );
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channelB.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Name.Should().Be("Hydrate 2");
        List<DomainTimer> rows = await _h.Db.Timers.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(3);
        rows.Single(t => t.Id == ownOfA).Messages.Should().Equal("A says drink");
        rows.Single(t => t.Id == ownOfB).Messages.Should().Equal("B says drink");
        rows.Single(t => t.Id == ownOfB).PlatformSourceDefinitionId.Should().BeNull();
        DomainTimer installed = rows.Single(t => t.Id == result.Value.EntityId);
        installed.BroadcasterId.Should().Be(channelB.Id);
        installed.Name.Should().Be("Hydrate 2");
        rows.Count(t => t.BroadcasterId == channelA.Id).Should().Be(1);
    }

    [Theory]
    [InlineData("""{"name":"","messages":["hi"]}""")]
    [InlineData("""{"name":"x","messages":["hi"],"intervalMinutes":0}""")]
    [InlineData("""{"name":"x","messages":["hi"],"intervalMinutes":1441}""")]
    [InlineData("""{"name":"x","messages":["hi"],"minChatActivity":-1}""")]
    [InlineData("""{"name":"x","messages":["  "]}""")]
    [InlineData("""{"name":"x","messages":["hi {args.1}"]}""")]
    [InlineData("[1,2]")]
    public async Task Authoring_rejects_a_bad_payload_and_stores_nothing(string payload)
    {
        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.Timer, "bad", "Bad", null, payload)
            );

        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_is_denied_without_the_timer_write_action()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);
        _h.AllowChannelAction(false);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.ErrorCode.Should().Be("FORBIDDEN");
        await _h
            .Authorization.Received()
            .AuthorizeActionAsync(
                _h.CallerUserId,
                channel.Id,
                "timers:write",
                Arg.Any<CancellationToken>()
            );
        (await _h.Db.Timers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_respects_the_channels_timer_limit()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(_installer, "hydrate", HydrateTemplate);
        AllowQuota(false);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.IsFailure.Should().BeTrue();
        (await _h.Db.Timers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_pipeline_only_timer_binds_only_a_pipeline_the_installing_channel_owns()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        Pipeline pipelineOfA = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelA.Id,
            Name = "a-flow",
        };
        Pipeline pipelineOfB = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelB.Id,
            Name = "b-flow",
        };
        _h.Db.Pipelines.AddRange(pipelineOfA, pipelineOfB);
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "ad-break",
            """{"name":"Ad break","messages":[],"intervalMinutes":60}"""
        );
        PlatformTemplateCatalogService catalog = _h.Catalog(_installer);

        Result<InstalledPlatformTemplateDto> noPipeline = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(null)
        );
        Result<InstalledPlatformTemplateDto> foreignPipeline = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(pipelineOfA.Id)
        );

        noPipeline.ErrorCode.Should().Be("VALIDATION_FAILED");
        foreignPipeline.ErrorCode.Should().Be(PipelineOwnership.ErrorCode);
        (await _h.Db.Timers.CountAsync()).Should().Be(0);

        Result<InstalledPlatformTemplateDto> ownPipeline = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(pipelineOfB.Id)
        );

        ownPipeline.IsSuccess.Should().BeTrue(ownPipeline.ErrorMessage);
        DomainTimer row = await _h.Db.Timers.SingleAsync();
        row.BroadcasterId.Should().Be(channelB.Id);
        row.PipelineId.Should().Be(pipelineOfB.Id);
        row.Messages.Should().BeEmpty();
        row.IntervalMinutes.Should().Be(60);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();
}
