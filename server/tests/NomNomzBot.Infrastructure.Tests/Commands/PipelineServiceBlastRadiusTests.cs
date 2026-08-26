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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S-CONSEQ: <see cref="PipelineService.GetBlastRadiusAsync"/> must report the REAL, counted rows
/// that reference a pipeline — never an estimate — so the dashboard delete confirmation can show
/// exactly what it disables before the operator commits to it.
/// </summary>
public sealed class PipelineServiceBlastRadiusTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000c0901");

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

    private static object GraphWithOneStep() =>
        new
        {
            steps = new object[] { new { action = new { type = "send_message", message = "hi" } } },
        };

    private static PipelineService BuildService(AuthDbContext db) =>
        new(
            db,
            new PassThroughUnitOfWork(),
            Substitute.For<IEventBus>(),
            new CommandConfigValidator(
                [new FakeAction { ActionType = "send_message" }],
                new TemplateHelperValidator()
            ),
            Substitute.For<IChannelRegistry>()
        );

    [Fact]
    public async Task GetBlastRadiusAsync_counts_every_real_dependent_of_a_referenced_pipeline()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PipelineService service = BuildService(db);

        Result<PipelineDto> pipeline = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "raid-flow", GraphJsonCache = GraphWithOneStep() }
        );
        pipeline.IsSuccess.Should().BeTrue();
        Guid pipelineId = pipeline.Value.Id;

        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                Name = "!raid",
                NameNormalized = "raid",
                PipelineId = pipelineId,
            }
        );
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                Name = "!raidme",
                NameNormalized = "raidme",
                PipelineId = pipelineId,
            }
        );
        db.ChatTriggers.Add(
            new ChatTrigger
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                Pattern = "raid hype",
                PipelineId = pipelineId,
            }
        );
        db.Timers.Add(
            new Timer
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                Name = "raid-reminder",
                PipelineId = pipelineId,
            }
        );
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                EventType = "channel.raid",
                ResponseType = "pipeline",
                PipelineId = pipelineId,
            }
        );
        // A row referencing a DIFFERENT pipeline must never be counted into this one's radius.
        Result<PipelineDto> otherPipeline = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "unrelated-flow", GraphJsonCache = GraphWithOneStep() }
        );
        db.Commands.Add(
            new Command
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = ChannelA,
                Name = "!other",
                NameNormalized = "other",
                PipelineId = otherPipeline.Value.Id,
            }
        );
        await db.SaveChangesAsync();

        Result<PipelineBlastRadiusDto> result = await service.GetBlastRadiusAsync(
            ChannelA.ToString(),
            pipelineId
        );

        result.IsSuccess.Should().BeTrue();
        PipelineBlastRadiusDto radius = result.Value;
        radius.CommandCount.Should().Be(2);
        radius.CommandNames.Should().BeEquivalentTo("!raid", "!raidme");
        radius.ChatTriggerCount.Should().Be(1);
        radius.ChatTriggerPatterns.Should().BeEquivalentTo("raid hype");
        radius.TimerCount.Should().Be(1);
        radius.TimerNames.Should().BeEquivalentTo("raid-reminder");
        radius.EventResponseCount.Should().Be(1);
        radius.EventResponseEventTypes.Should().BeEquivalentTo("channel.raid");
        radius.TotalReferences.Should().Be(5);
    }

    [Fact]
    public async Task GetBlastRadiusAsync_reports_zero_for_a_pipeline_nothing_references()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PipelineService service = BuildService(db);

        Result<PipelineDto> pipeline = await service.CreateAsync(
            ChannelA.ToString(),
            new() { Name = "lonely-flow", GraphJsonCache = GraphWithOneStep() }
        );

        Result<PipelineBlastRadiusDto> result = await service.GetBlastRadiusAsync(
            ChannelA.ToString(),
            pipeline.Value.Id
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalReferences.Should().Be(0);
    }

    [Fact]
    public async Task GetBlastRadiusAsync_fails_for_a_pipeline_that_does_not_exist()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        PipelineService service = BuildService(db);

        Result<PipelineBlastRadiusDto> result = await service.GetBlastRadiusAsync(
            ChannelA.ToString(),
            Guid.CreateVersion7()
        );

        result.IsFailure.Should().BeTrue();
    }
}
