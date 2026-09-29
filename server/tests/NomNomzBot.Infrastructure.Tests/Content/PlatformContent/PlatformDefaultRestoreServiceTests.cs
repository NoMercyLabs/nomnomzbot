// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// One channel's "Restore default" for a template-installed timer and a platform-seeded pipeline: the preview
/// names what differs before anything is written, the restore writes the platform default through the same
/// save paths a publish uses, and afterwards the row reads as untouched again. A row the channel made itself
/// has no default, and another channel's row is never reachable.
/// </summary>
public sealed class PlatformDefaultRestoreServiceTests : IAsyncDisposable
{
    private const string HydrateTemplate =
        """{"name":"Hydrate","messages":["Drink some water!","Stretch your legs."],"intervalMinutes":45,"minChatActivity":5,"fireOnce":false,"isEnabled":true}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly TimerTemplateInstaller _timerInstaller;
    private readonly PipelineService _pipelines;
    private readonly PlatformDefaultRestoreService _sut;

    private sealed class MarkerAction : ICommandAction
    {
        public string ActionType => "record_marker";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            Application.Abstractions.Pipeline.ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    public PlatformDefaultRestoreServiceTests()
    {
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();
        quota
            .GetCurrentCountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(0L));
        quota
            .CheckAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(new QuotaCheckDto(true, call.ArgAt<string>(1), 0, 10, 10))
            );
        TimerManagementService timers = new(
            _h.Db,
            Substitute.For<IEventBus>(),
            quota,
            new TemplateHelperValidator()
        );
        _timerInstaller = new(_h.Db, timers, new TemplateHelperValidator());
        _pipelines = new(
            _h.Db,
            new TestUnitOfWork(_h.Db),
            Substitute.For<IEventBus>(),
            new CommandConfigValidator([new MarkerAction()], new TemplateHelperValidator()),
            Substitute.For<IChannelRegistry>()
        );
        _sut = new(_h.Db, new TestUnitOfWork(_h.Db), _pipelines, [_timerInstaller]);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    // --- Timers ------------------------------------------------------------------------------------------

    private async Task<(Channel Channel, DomainTimer Row)> InstallHydrateAsync()
    {
        Channel channel = await _h.AddChannelAsync("streamer-r");
        Guid definitionId = await _h.PublishTemplateAsync(
            _timerInstaller,
            "hydrate",
            HydrateTemplate
        );
        Result<InstalledPlatformTemplateDto> installed = await _h.Catalog(_timerInstaller)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));
        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        DomainTimer row = await _h.Db.Timers.SingleAsync(t => t.Id == installed.Value.EntityId);
        return (channel, row);
    }

    [Fact]
    public async Task An_edited_timer_previews_every_changed_field_then_restores_to_the_template()
    {
        (Channel channel, DomainTimer row) = await InstallHydrateAsync();
        row.Name = "Water";
        row.Messages = ["My own line"];
        row.IntervalMinutes = 10;
        await _h.Db.SaveChangesAsync();

        PlatformDefaultPreviewDto preview = (
            await _sut.PreviewAsync(channel.Id, PlatformContentKinds.Timer, row.Id)
        ).Value;

        preview.IsEdited.Should().BeTrue();
        preview.InstalledVersion.Should().Be(1);
        preview.DefaultVersion.Should().Be(1);
        preview
            .Changes.Should()
            .BeEquivalentTo([
                new PlatformDefaultChangeDto("name", "Water", "Hydrate"),
                new PlatformDefaultChangeDto(
                    "messages",
                    "My own line",
                    "Drink some water!\nStretch your legs."
                ),
                new PlatformDefaultChangeDto("interval", "10", "45"),
            ]);

        Result<PlatformDefaultPreviewDto> restored = await _sut.RestoreAsync(
            channel.Id,
            PlatformContentKinds.Timer,
            row.Id
        );

        restored.IsSuccess.Should().BeTrue(restored.ErrorMessage);
        restored.Value.Changes.Should().BeEmpty();
        restored.Value.IsEdited.Should().BeFalse();
        DomainTimer after = await _h.Db.Timers.AsNoTracking().SingleAsync(t => t.Id == row.Id);
        after.Name.Should().Be("Hydrate");
        after.Messages.Should().Equal("Drink some water!", "Stretch your legs.");
        after.IntervalMinutes.Should().Be(45);
        after.PlatformSourceHash.Should().Be(TimerTemplatePayload.FromEntity(after).ComputeHash());
    }

    [Fact]
    public async Task A_timer_the_channel_made_has_no_default_and_stays_untouched()
    {
        Channel channel = await _h.AddChannelAsync("streamer-own");
        DomainTimer own = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel.Id,
            Name = "Mine",
            Messages = ["mine"],
        };
        _h.Db.Timers.Add(own);
        await _h.Db.SaveChangesAsync();

        Result<PlatformDefaultPreviewDto> preview = await _sut.PreviewAsync(
            channel.Id,
            PlatformContentKinds.Timer,
            own.Id
        );
        Result<PlatformDefaultPreviewDto> restore = await _sut.RestoreAsync(
            channel.Id,
            PlatformContentKinds.Timer,
            own.Id
        );

        preview.ErrorCode.Should().Be("NOT_PLATFORM_CONTENT");
        restore.ErrorCode.Should().Be("NOT_PLATFORM_CONTENT");
        (await _h.Db.Timers.AsNoTracking().SingleAsync(t => t.Id == own.Id))
            .Messages.Should()
            .Equal("mine");
    }

    [Fact]
    public async Task Another_channels_timer_is_not_found()
    {
        (_, DomainTimer row) = await InstallHydrateAsync();
        Channel other = await _h.AddChannelAsync("someone-else");

        Result<PlatformDefaultPreviewDto> restore = await _sut.RestoreAsync(
            other.Id,
            PlatformContentKinds.Timer,
            row.Id
        );

        restore.ErrorCode.Should().Be("NOT_FOUND");
    }

    // --- Pipelines ---------------------------------------------------------------------------------------

    private static string Graph(params string[] markers) =>
        JsonSerializer.Serialize(
            new
            {
                steps = markers
                    .Select(m => new { action = new { type = "record_marker", marker = m } })
                    .ToArray(),
            }
        );

    [Fact]
    public async Task An_edited_seeded_pipeline_previews_its_step_change_then_restores_the_default_steps()
    {
        Channel channel = await _h.AddChannelAsync("raider");
        PlatformContentDefinition definition = new()
        {
            Kind = PlatformContentKinds.Pipeline,
            Key = "raid_out",
            DisplayName = "Raid out",
            CreatedAt = DateTime.UtcNow,
            CreatedByPrincipalId = Guid.Empty,
        };
        _h.Db.PlatformContentDefinitions.Add(definition);
        string defaultPayload = Graph("default-step");
        PlatformContentVersion v1 = new()
        {
            DefinitionId = definition.Id,
            Version = 1,
            ContentHash = PlatformContentHash.ComputeHash(defaultPayload),
            PayloadJson = defaultPayload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = Guid.Empty,
            PublishedAt = DateTime.UtcNow,
        };
        _h.Db.PlatformContentVersions.Add(v1);
        definition.CurrentVersionId = v1.Id;
        await _h.Db.SaveChangesAsync();

        // Seeded exactly as the default, stamped with its own hash (the provenance backfill's rule).
        PipelineDto created = (
            await _pipelines.CreateAsync(
                channel.Id.ToString(),
                new CreatePipelineDto
                {
                    Name = "Raid out",
                    TriggerKind = "command",
                    GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(defaultPayload),
                }
            )
        ).Value;
        PipelineEntity row = await _h.Db.Pipelines.SingleAsync(p => p.Id == created.Id);
        row.PlatformSourceDefinitionId = definition.Id;
        row.PlatformSourceVersion = 1;
        row.PlatformSourceHash = PlatformContentHash.ComputeHash(row.GraphJsonCache);
        await _h.Db.SaveChangesAsync();

        (await _sut.PreviewAsync(channel.Id, PlatformContentKinds.Pipeline, row.Id))
            .Value.Changes.Should()
            .BeEmpty("an untouched seeded pipeline has nothing to restore");

        // The streamer edits it: two steps of their own.
        (
            await _pipelines.UpdateAsync(
                channel.Id.ToString(),
                row.Id,
                new UpdatePipelineDto
                {
                    GraphJsonCache = JsonSerializer.Deserialize<JsonElement>(
                        Graph("mine-1", "mine-2")
                    ),
                }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        // Meanwhile the platform publishes a newer default; "Restore default" means the CURRENT default.
        string v2Payload = Graph("default-step-v2");
        PlatformContentVersion v2 = new()
        {
            DefinitionId = definition.Id,
            Version = 2,
            ContentHash = PlatformContentHash.ComputeHash(v2Payload),
            PayloadJson = v2Payload,
            DraftedAt = DateTime.UtcNow,
            DraftedByPrincipalId = Guid.Empty,
            PublishedAt = DateTime.UtcNow,
        };
        _h.Db.PlatformContentVersions.Add(v2);
        definition.CurrentVersionId = v2.Id;
        await _h.Db.SaveChangesAsync();

        PlatformDefaultPreviewDto preview = (
            await _sut.PreviewAsync(channel.Id, PlatformContentKinds.Pipeline, row.Id)
        ).Value;
        preview.IsEdited.Should().BeTrue();
        preview.DefaultName.Should().Be("Raid out");
        preview.InstalledVersion.Should().Be(1);
        preview.DefaultVersion.Should().Be(2);
        preview.Changes.Should().BeEquivalentTo([new PlatformDefaultChangeDto("steps", "2", "1")]);

        Result<PlatformDefaultPreviewDto> restored = await _sut.RestoreAsync(
            channel.Id,
            PlatformContentKinds.Pipeline,
            row.Id
        );

        restored.IsSuccess.Should().BeTrue(restored.ErrorMessage);
        restored.Value.Changes.Should().BeEmpty();
        restored.Value.IsEdited.Should().BeFalse();
        restored.Value.InstalledVersion.Should().Be(2);
        List<PipelineStep> steps = await _h
            .Db.PipelineSteps.AsNoTracking()
            .Where(s => s.PipelineId == row.Id)
            .ToListAsync();
        steps.Should().ContainSingle().Which.ConfigJson.Should().Contain("default-step-v2");
    }
}
