// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Saving a pipeline tells the person which browser sources its steps need that are not open now. The save
/// itself always succeeds; the answer rides on the saved pipeline as <see cref="PipelineDto.MissingSources"/>.
/// </summary>
public sealed class PipelineServiceMissingSourcesTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0a00");

    private sealed class FakeAction : ICommandAction
    {
        public required string ActionType { get; init; }

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private static PipelineService Build(
        IOverlayPresenceRegistry presence,
        PipelineTestRunDbContext db
    ) =>
        new(
            db,
            new PassThroughUnitOfWork(),
            Substitute.For<IEventBus>(),
            new CommandConfigValidator(
                [
                    new FakeAction { ActionType = "send_message" },
                    new FakeAction { ActionType = "tts_synthesize" },
                    new FakeAction { ActionType = "play_sound" },
                ],
                new TemplateHelperValidator()
            ),
            Substitute.For<IChannelRegistry>(),
            presence
        );

    private static IOverlayPresenceRegistry OnlyAudioOpen()
    {
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsOverlayConnected(Broadcaster).Returns(true);
        presence.IsAudioSourceConnected(Broadcaster).Returns(true);
        presence.IsTtsSourceConnected(Broadcaster).Returns(false);
        return presence;
    }

    private static JsonElement Graph(params string[] actionTypes) =>
        JsonSerializer.SerializeToElement(
            new { steps = actionTypes.Select(type => new { action = new { type } }).ToArray() }
        );

    [Fact]
    public async Task Create_with_a_tts_step_and_a_sound_step_names_only_the_tts_source_when_only_audio_is_open()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        PipelineService service = Build(OnlyAudioOpen(), db);

        Result<PipelineDto> saved = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "speaks",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = Graph("tts_synthesize", "play_sound", "send_message"),
            }
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved
            .Value.MissingSources.Should()
            .BeEquivalentTo([new MissingSourceDto("tts_caption", ["tts_synthesize"])]);
    }

    [Fact]
    public async Task Update_reports_the_same_missing_source_and_the_save_still_persists_the_graph()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        PipelineService service = Build(OnlyAudioOpen(), db);
        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "later",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = Graph("send_message"),
            }
        );

        Result<PipelineDto> updated = await service.UpdateAsync(
            Broadcaster.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = Graph("tts_synthesize", "tts_synthesize") }
        );

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated
            .Value.MissingSources.Should()
            .BeEquivalentTo([new MissingSourceDto("tts_caption", ["tts_synthesize"])]);
        updated.Value.GraphJsonCache!.Value.GetProperty("steps").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task A_graph_with_no_overlay_steps_reports_nothing_missing()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        PipelineService service = Build(OnlyAudioOpen(), db);

        Result<PipelineDto> saved = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "plain",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = Graph("send_message"),
            }
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.MissingSources.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_needed_source_open_reports_nothing_missing()
    {
        IOverlayPresenceRegistry presence = OnlyAudioOpen();
        presence.IsTtsSourceConnected(Broadcaster).Returns(true);
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        PipelineService service = Build(presence, db);

        Result<PipelineDto> saved = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "all open",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = Graph("tts_synthesize", "play_sound"),
            }
        );

        saved.Value.MissingSources.Should().BeEmpty();
    }
}
