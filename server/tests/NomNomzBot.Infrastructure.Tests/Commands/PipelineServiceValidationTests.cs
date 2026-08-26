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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S007: <see cref="PipelineService.CreateAsync"/>/<see cref="PipelineService.UpdateAsync"/> must run every
/// incoming graph through <see cref="CommandConfigValidator"/> before it touches the database — the same
/// rules the optional editor "validate" endpoint enforces — so a client that skips that endpoint (automation,
/// marketplace import, a direct API call) can never persist a graph that only fails live inside the engine.
/// </summary>
public sealed class PipelineServiceValidationTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0701");

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

    private static (PipelineService Service, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CommandConfigValidator validator = new(
            [new FakeAction { ActionType = "send_message" }],
            new TemplateHelperValidator()
        );
        return (
            new(
                db,
                new PassThroughUnitOfWork(),
                Substitute.For<IEventBus>(),
                validator,
                Substitute.For<IChannelRegistry>()
            ),
            db
        );
    }

    private static object GraphWith(params object[] steps) =>
        JsonSerializer.SerializeToElement(new { steps });

    private static object ValidStep(string actionType = "send_message") =>
        new { action = new { type = actionType, message = "hi" } };

    [Fact]
    public async Task CreateAsync_rejects_unknown_action_type_and_persists_nothing()
    {
        (PipelineService service, AuthDbContext db) = Build();

        Result<PipelineDto> result = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "evil", GraphJsonCache = GraphWith(ValidStep("does_not_exist")) }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_ACTION_TYPE");
        (await db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_persists_a_valid_graph_and_round_trips_it_unchanged()
    {
        (PipelineService service, AuthDbContext db) = Build();

        Result<PipelineDto> result = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "good", GraphJsonCache = GraphWith(ValidStep()) }
        );

        result.IsSuccess.Should().BeTrue();
        (await db.Pipelines.CountAsync()).Should().Be(1);

        PipelineEntity stored = await db.Pipelines.SingleAsync();
        JsonDocument storedGraph = JsonDocument.Parse(stored.GraphJsonCache!);
        storedGraph
            .RootElement.GetProperty("steps")[0]
            .GetProperty("action")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("send_message");
    }

    [Fact]
    public async Task CreateAsync_rejects_a_graph_exceeding_the_step_cap()
    {
        (PipelineService service, AuthDbContext db) = Build();

        object[] tooManySteps = [.. Enumerable.Range(0, 101).Select(_ => ValidStep())];

        Result<PipelineDto> result = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "huge", GraphJsonCache = GraphWith(tooManySteps) }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("STEP_COUNT_EXCEEDED");
        (await db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_credential_shaped_config_value()
    {
        (PipelineService service, AuthDbContext db) = Build();

        object step = new { action = new { type = "send_message", token = "shh-secret" } };

        Result<PipelineDto> result = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "leaky", GraphJsonCache = GraphWith(step) }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("BANNED_CONFIG_KEY");
        (await db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_invalid_graph_and_keeps_the_previous_stored_graph()
    {
        (PipelineService service, AuthDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "keep-me", GraphJsonCache = GraphWith(ValidStep()) }
        );
        created.IsSuccess.Should().BeTrue();
        string originalGraph = (await db.Pipelines.SingleAsync()).GraphJsonCache!;

        Result<PipelineDto> updateResult = await service.UpdateAsync(
            Broadcaster.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = GraphWith(ValidStep("does_not_exist")) }
        );

        updateResult.IsSuccess.Should().BeFalse();
        updateResult.ErrorCode.Should().Be("UNKNOWN_ACTION_TYPE");

        PipelineEntity persisted = await db.Pipelines.SingleAsync();
        persisted.GraphJsonCache.Should().Be(originalGraph);
    }

    [Fact]
    public async Task UpdateAsync_accepts_a_valid_graph_and_persists_it()
    {
        (PipelineService service, AuthDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new() { Name = "swap-me", GraphJsonCache = GraphWith(ValidStep()) }
        );

        Result<PipelineDto> updateResult = await service.UpdateAsync(
            Broadcaster.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = GraphWith(ValidStep(), ValidStep()) }
        );

        updateResult.IsSuccess.Should().BeTrue();

        PipelineEntity persisted = await db.Pipelines.SingleAsync();
        JsonDocument
            .Parse(persisted.GraphJsonCache!)
            .RootElement.GetProperty("steps")
            .GetArrayLength()
            .Should()
            .Be(2);
    }
}
