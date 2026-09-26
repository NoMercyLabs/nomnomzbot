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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// The <c>event_response</c> platform-template kind, end to end: an admin authors and publishes a template,
/// a channel installs it, and the channel's real <see cref="EventResponse"/> row carries the template's
/// fields plus provenance. Runs the real <see cref="EventResponseService"/> save path.
/// </summary>
public sealed class PlatformTemplateEventResponseTests : IAsyncDisposable
{
    private const string FollowTemplate =
        """{"eventType":"channel.follow","responseType":"chat_message","message":"Welcome {user}!","metadata":{"tone":"warm"},"isEnabled":true}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly EventResponseTemplateInstaller _installer;

    public PlatformTemplateEventResponseTests()
    {
        EventResponseService eventResponses = new(
            _h.Db,
            Substitute.For<IEventBus>(),
            new TemplateHelperValidator()
        );
        _installer = new(_h.Db, eventResponses, new TemplateHelperValidator());
    }

    [Fact]
    public async Task Install_writes_the_template_into_the_channels_event_response_with_provenance()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "warm-follow",
            FollowTemplate
        );

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        EventResponse row = await _h.Db.EventResponses.SingleAsync();
        row.Id.Should().Be(result.Value.EntityId);
        row.BroadcasterId.Should().Be(channel.Id);
        row.EventType.Should().Be("channel.follow");
        row.ResponseType.Should().Be("chat_message");
        row.Message.Should().Be("Welcome {user}!");
        row.MetadataJson.Should().Equal(new Dictionary<string, string> { ["tone"] = "warm" });
        row.IsEnabled.Should().BeTrue();
        row.PipelineId.Should().BeNull();
        row.PlatformSourceDefinitionId.Should().Be(definitionId);
        row.PlatformSourceVersion.Should().Be(1);
        row.PlatformSourceHash.Should()
            .Be(EventResponseTemplatePayload.FromEntity(row).ComputeHash());
        row.PlatformSourceSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Install_replaces_only_the_installing_channels_row_for_that_event()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        _h.Db.EventResponses.AddRange(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelA.Id,
                EventType = "channel.follow",
                Message = "A greeting of its own",
                IsEnabled = false,
            },
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelB.Id,
                EventType = "channel.follow",
                Message = "B old greeting",
                IsEnabled = false,
            }
        );
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "warm-follow",
            FollowTemplate
        );

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channelB.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        List<EventResponse> rows = await _h.Db.EventResponses.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        EventResponse a = rows.Single(r => r.BroadcasterId == channelA.Id);
        a.Message.Should().Be("A greeting of its own");
        a.IsEnabled.Should().BeFalse();
        a.PlatformSourceDefinitionId.Should().BeNull();
        EventResponse b = rows.Single(r => r.BroadcasterId == channelB.Id);
        b.Message.Should().Be("Welcome {user}!");
        b.IsEnabled.Should().BeTrue();
        b.PlatformSourceDefinitionId.Should().Be(definitionId);
    }

    [Theory]
    [InlineData("""{"eventType":"channel.not_real","message":"hi"}""")]
    [InlineData("""{"eventType":"channel.follow","responseType":"chat_message"}""")]
    [InlineData("""{"eventType":"channel.follow","responseType":"shout","message":"hi"}""")]
    [InlineData("""{"eventType":"channel.follow","message":"hi {args.1}"}""")]
    [InlineData("not json")]
    public async Task Authoring_rejects_a_bad_payload_and_stores_nothing(string payload)
    {
        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.EventResponse, "bad", "Bad", null, payload)
            );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Drafting_a_bad_version_is_rejected_and_stores_no_version()
    {
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "warm-follow",
            FollowTemplate
        );

        Result<PlatformContentVersionDto> drafted = await _h.AdminService(_installer)
            .DraftVersionAsync(
                _h.ActingPrincipalId,
                definitionId,
                new("""{"eventType":"channel.nope","message":"x"}""", null)
            );

        drafted.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentVersions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Authoring_is_denied_without_the_platform_content_permission()
    {
        _h.AllowPlatform(false);

        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.EventResponse, "warm", "Warm", null, FollowTemplate)
            );

        created.ErrorCode.Should().Be("FORBIDDEN");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_is_denied_without_the_event_response_write_action()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "warm-follow",
            FollowTemplate
        );
        _h.AllowChannelAction(false);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.ErrorCode.Should().Be("FORBIDDEN");
        await _h
            .Authorization.Received()
            .AuthorizeActionAsync(
                _h.CallerUserId,
                channel.Id,
                "eventresponses:write",
                Arg.Any<CancellationToken>()
            );
        (await _h.Db.EventResponses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_pipeline_template_binds_only_a_pipeline_the_installing_channel_owns()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        Pipeline pipelineOfA = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelA.Id,
            Name = "a-raid-flow",
        };
        Pipeline pipelineOfB = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelB.Id,
            Name = "b-raid-flow",
        };
        _h.Db.Pipelines.AddRange(pipelineOfA, pipelineOfB);
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "raid-pipeline",
            """{"eventType":"channel.raid","responseType":"pipeline"}"""
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
        (await _h.Db.EventResponses.CountAsync()).Should().Be(0);

        Result<InstalledPlatformTemplateDto> ownPipeline = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(pipelineOfB.Id)
        );

        ownPipeline.IsSuccess.Should().BeTrue(ownPipeline.ErrorMessage);
        EventResponse row = await _h.Db.EventResponses.SingleAsync();
        row.BroadcasterId.Should().Be(channelB.Id);
        row.ResponseType.Should().Be("pipeline");
        row.PipelineId.Should().Be(pipelineOfB.Id);
        row.Message.Should().BeNull();
    }

    [Fact]
    public async Task The_catalogue_lists_only_published_live_templates_of_the_kind()
    {
        Guid published = await _h.PublishTemplateAsync(_installer, "warm-follow", FollowTemplate);
        Guid retired = await _h.PublishTemplateAsync(_installer, "old-follow", FollowTemplate);
        Result<PlatformContentDefinitionDto> draftOnly = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.EventResponse, "draft", "Draft", null, FollowTemplate)
            );
        draftOnly.IsSuccess.Should().BeTrue();
        await _h.AdminService(_installer).RetireDefinitionAsync(_h.ActingPrincipalId, retired);
        Channel channel = await _h.AddChannelAsync("streamer-b");

        Result<PagedList<PlatformTemplateDto>> listed = await _h.Catalog(_installer)
            .ListAsync(PlatformContentKinds.EventResponse, 1, 25);
        Result<InstalledPlatformTemplateDto> installRetired = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, retired, new(null));
        Result<InstalledPlatformTemplateDto> installDraft = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, draftOnly.Value.Id, new(null));

        listed.IsSuccess.Should().BeTrue();
        PlatformTemplateDto only = listed.Value.Items.Should().ContainSingle().Subject;
        only.DefinitionId.Should().Be(published);
        only.Kind.Should().Be(PlatformContentKinds.EventResponse);
        only.Version.Should().Be(1);
        only.PayloadJson.Should().Be(FollowTemplate);
        installRetired.ErrorCode.Should().Be("NOT_FOUND");
        installDraft.ErrorCode.Should().Be("NOT_FOUND");
        (await _h.Db.EventResponses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_catalogue_refuses_a_kind_that_is_not_installable()
    {
        Result<PagedList<PlatformTemplateDto>> listed = await _h.Catalog(_installer)
            .ListAsync(PlatformContentKinds.Command, 1, 25);

        listed.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task The_admin_event_catalogue_is_the_server_catalogue_and_is_iam_gated()
    {
        Result<IReadOnlyList<EventResponsePresetDto>> allowed = await _h.AdminService(_installer)
            .ListEventResponseTypesAsync(_h.ActingPrincipalId);
        _h.AllowPlatform(false);
        Result<IReadOnlyList<EventResponsePresetDto>> denied = await _h.AdminService(_installer)
            .ListEventResponseTypesAsync(_h.ActingPrincipalId);

        allowed.IsSuccess.Should().BeTrue();
        allowed
            .Value.Select(p => p.EventType)
            .Should()
            .Equal(EventResponsePresetCatalog.EventTypes);
        allowed.Value.Select(p => p.EventType).Should().Contain("channel.follow");
        denied.ErrorCode.Should().Be("FORBIDDEN");
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();
}
