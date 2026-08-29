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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S-PIPE-WRITE-SYMMETRY. <see cref="Platform.Pipeline.PipelineEngine"/> executes a bound pipeline
/// from its normalized <see cref="PipelineStep"/>/<see cref="PipelineStepCondition"/> rows FIRST
/// (falling back to <c>GraphJsonCache</c> only when no rows exist — <c>PipelineEngine.cs</c> "Step
/// source priority") — the rows are execution truth, not the cache. Before this slice,
/// <see cref="PipelineService.CreateAsync"/>/<see cref="PipelineService.UpdateAsync"/> wrote ONLY
/// <c>GraphJsonCache</c>, so every dashboard-authored pipeline had a permanently empty row set and
/// silently ran on the cache-fallback path — the asymmetry that turned a wire-binding bug into
/// unrecoverable data loss (both representations landed empty at once). These tests prove the two
/// representations are now written together and can never diverge.
/// </summary>
public sealed class PipelineServiceWriteSymmetryTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0900");

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

    private static (PipelineService Service, PipelineTestRunDbContext Db) Build()
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        CommandConfigValidator validator = new(
            [
                new FakeAction { ActionType = "send_message" },
                new FakeAction { ActionType = "timeout_user" },
                new FakeAction { ActionType = "shoutout" },
            ],
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

    private static PipelineEngine BuildEngine(PipelineTestRunDbContext db) =>
        new(
            db,
            Substitute.For<IChannelRegistry>(),
            [
                new FakeAction { ActionType = "send_message" },
                new FakeAction { ActionType = "timeout_user" },
                new FakeAction { ActionType = "shoutout" },
            ],
            [],
            NSubstitute.Substitute.For<NomNomzBot.Application.Abstractions.Templating.ITemplateResolver>(),
            NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<PipelineEngine>>(),
            TimeProvider.System
        );

    [Fact]
    public async Task CreateAsync_persists_normalized_step_and_condition_rows_matching_the_graph()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "dashboard-authored",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "send_message", message = "hi" } },
                            new
                            {
                                action = new { type = "timeout_user", seconds = 30 },
                                condition = new
                                {
                                    type = "user_role",
                                    @operator = "eq",
                                    left = "role",
                                    right = "moderator",
                                    negate = false,
                                },
                            },
                        },
                    }
                ),
            }
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        List<PipelineStep> steps = await db
            .PipelineSteps.Where(s => s.PipelineId == created.Value.Id)
            .Include(s => s.Conditions)
            .OrderBy(s => s.Order)
            .ToListAsync();

        steps.Should().HaveCount(2, "the cache write must be mirrored into normalized rows");
        steps[0].ActionType.Should().Be("send_message");
        steps[0].Order.Should().Be(0);
        steps[0].Conditions.Should().BeEmpty();

        steps[1].ActionType.Should().Be("timeout_user");
        steps[1].Order.Should().Be(1);
        steps[1].Conditions.Should().ContainSingle();
        PipelineStepCondition condition = steps[1].Conditions.Single();
        condition.ConditionType.Should().Be("user_role");
        condition.LeftOperand.Should().Be("role");
        condition.RightOperand.Should().Be("moderator");
        condition.Operator.Should().Be("eq");
        condition.Negate.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_keeps_rows_in_sync_including_removed_and_reordered_steps()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "will-be-edited",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "send_message" } },
                            new { action = new { type = "timeout_user" } },
                            new { action = new { type = "shoutout" } },
                        },
                    }
                ),
            }
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        List<PipelineStep> beforeUpdate = await db
            .PipelineSteps.Where(s => s.PipelineId == created.Value.Id)
            .ToListAsync();
        beforeUpdate.Should().HaveCount(3);
        Guid removedStepId = beforeUpdate.Single(s => s.ActionType == "timeout_user").Id;

        // Edit: drop timeout_user, reverse the remaining two steps' order.
        Result<PipelineDto> updated = await service.UpdateAsync(
            Broadcaster.ToString(),
            created.Value.Id,
            new()
            {
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "shoutout" } },
                            new { action = new { type = "send_message" } },
                        },
                    }
                ),
            }
        );

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);

        List<PipelineStep> afterUpdate = await db
            .PipelineSteps.Where(s => s.PipelineId == created.Value.Id)
            .OrderBy(s => s.Order)
            .ToListAsync();

        afterUpdate.Should().HaveCount(2, "the removed step must not survive as an orphan row");
        afterUpdate.Select(s => s.Id).Should().NotContain(removedStepId);
        afterUpdate[0].ActionType.Should().Be("shoutout");
        afterUpdate[0].Order.Should().Be(0);
        afterUpdate[1].ActionType.Should().Be("send_message");
        afterUpdate[1].Order.Should().Be(1);

        bool anyOrphanCondition = await db.PipelineStepConditions.AnyAsync(c =>
            !afterUpdate.Select(s => s.Id).Contains(c.PipelineStepId)
            && db.PipelineSteps.Any(s =>
                s.Id == c.PipelineStepId && s.PipelineId == created.Value.Id
            )
        );
        anyOrphanCondition.Should().BeFalse();
    }

    [Fact]
    public async Task Round_trip_graph_rebuilt_from_normalized_rows_matches_the_stored_cache()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "divergence-guard",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "send_message", message = "yo" } },
                            new
                            {
                                action = new { type = "timeout_user", seconds = 10 },
                                condition = new
                                {
                                    type = "user_role",
                                    @operator = "eq",
                                    left = "role",
                                    right = "vip",
                                    negate = true,
                                },
                            },
                        },
                    }
                ),
            }
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        // Force the read path to ignore the cache and rebuild purely from the normalized rows —
        // this is the guard: if either write path (cache OR rows) were skipped, this would fail.
        PipelineEntity entity = await db.Pipelines.SingleAsync(p => p.Id == created.Value.Id);
        string storedCache = entity.GraphJsonCache!;
        entity.GraphJsonCache = null;
        await db.SaveChangesAsync();

        Result<PipelineDto> rebuilt = await service.GetAsync(
            Broadcaster.ToString(),
            created.Value.Id
        );
        rebuilt.IsSuccess.Should().BeTrue(rebuilt.ErrorMessage);

        using JsonDocument storedDoc = JsonDocument.Parse(storedCache);
        JsonElement storedSteps = storedDoc.RootElement.GetProperty("steps");
        JsonElement rebuiltSteps = rebuilt.Value.GraphJsonCache!.Value.GetProperty("steps");

        rebuiltSteps.GetArrayLength().Should().Be(storedSteps.GetArrayLength());
        for (int i = 0; i < storedSteps.GetArrayLength(); i++)
        {
            storedSteps[i]
                .GetProperty("action")
                .GetProperty("type")
                .GetString()
                .Should()
                .Be(rebuiltSteps[i].GetProperty("action").GetProperty("type").GetString());
        }

        // Second step carries a condition — assert it round-tripped too.
        JsonElement storedCondition = storedSteps[1].GetProperty("condition");
        JsonElement rebuiltCondition = rebuiltSteps[1].GetProperty("condition");
        storedCondition
            .GetProperty("left")
            .GetString()
            .Should()
            .Be(rebuiltCondition.GetProperty("left").GetString());
        storedCondition
            .GetProperty("right")
            .GetString()
            .Should()
            .Be(rebuiltCondition.GetProperty("right").GetString());
        storedCondition
            .GetProperty("negate")
            .GetBoolean()
            .Should()
            .Be(rebuiltCondition.GetProperty("negate").GetBoolean());
    }

    [Fact]
    public async Task Engine_executes_the_dashboard_created_pipeline_from_its_normalized_rows()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "executable",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "send_message" } },
                            new { action = new { type = "shoutout" } },
                        },
                    }
                ),
            }
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        // Wipe the cache: the engine must still execute both steps correctly by loading them from
        // the normalized rows this slice now populates — proving the rows alone are sufficient, and
        // that dual-write did not change what the engine actually runs.
        PipelineEntity entity = await db.Pipelines.SingleAsync(p => p.Id == created.Value.Id);
        entity.GraphJsonCache = null;
        await db.SaveChangesAsync();

        PipelineEngine engine = BuildEngine(db);
        PipelineExecutionResult result = await engine.ExecuteAsync(
            new PipelineRequest
            {
                BroadcasterId = Broadcaster,
                PipelineId = created.Value.Id,
                PipelineJson = "{}",
                TriggeredByUserId = Guid.NewGuid().ToString(),
                TriggeredByDisplayName = "tester",
            }
        );

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result.StepsExecuted.Should().Be(2);
        result.Total.Should().Be(2);
    }

    // ── S046-branching-prereq — the wire graph carries tree-nesting fields ─────────────────────

    [Fact]
    public async Task Flat_pipeline_round_trips_through_BuildGraph_with_unchanged_execution_semantics()
    {
        (PipelineService service, PipelineTestRunDbContext db) = Build();

        Result<PipelineDto> created = await service.CreateAsync(
            Broadcaster.ToString(),
            new()
            {
                Name = "flat-regression-guard",
                TriggerKind = "command",
                IsEnabled = true,
                GraphJsonCache = JsonSerializer.SerializeToElement(
                    new
                    {
                        steps = new object[]
                        {
                            new { action = new { type = "send_message", message = "hi" } },
                            new
                            {
                                action = new { type = "timeout_user", seconds = 30 },
                                condition = new
                                {
                                    type = "user_role",
                                    @operator = "eq",
                                    left = "role",
                                    right = "moderator",
                                    negate = false,
                                },
                            },
                        },
                    }
                ),
            }
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);

        List<PipelineStep> steps = await db
            .PipelineSteps.Where(s => s.PipelineId == created.Value.Id)
            .Include(s => s.Conditions)
            .OrderBy(s => s.Order)
            .ToListAsync();

        // Rebuild the wire graph from the persisted rows a second time (the same reconstruction
        // GetAsync uses) — a flat, never-nested pipeline must show no ParentStepId/Branch/BlockKind
        // and the same action/condition/order as before this slice added those fields.
        JsonElement graph = PipelineGraphBuilder.BuildGraph(steps);
        JsonElement rebuiltSteps = graph.GetProperty("steps");

        rebuiltSteps.GetArrayLength().Should().Be(2);
        rebuiltSteps[0].GetProperty("parent_step_id").ValueKind.Should().Be(JsonValueKind.Null);
        rebuiltSteps[0].GetProperty("branch").ValueKind.Should().Be(JsonValueKind.Null);
        rebuiltSteps[0].GetProperty("block_kind").ValueKind.Should().Be(JsonValueKind.Null);
        rebuiltSteps[0].GetProperty("order").GetInt32().Should().Be(0);
        rebuiltSteps[0]
            .GetProperty("action")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("send_message");

        rebuiltSteps[1].GetProperty("order").GetInt32().Should().Be(1);
        rebuiltSteps[1]
            .GetProperty("action")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("timeout_user");
        rebuiltSteps[1]
            .GetProperty("condition")
            .GetProperty("left")
            .GetString()
            .Should()
            .Be("role");

        // Re-save the rebuilt graph — this is the regression guard: a flat pipeline saved from a
        // graph that now carries the new (always-null-here) fields must still produce the exact
        // same two rows in the exact same order, proving the reverse-parse path is additive only.
        Result<PipelineDto> resaved = await service.UpdateAsync(
            Broadcaster.ToString(),
            created.Value.Id,
            new() { GraphJsonCache = graph }
        );
        resaved.IsSuccess.Should().BeTrue(resaved.ErrorMessage);

        List<PipelineStep> stepsAfterResave = await db
            .PipelineSteps.Where(s => s.PipelineId == created.Value.Id)
            .Include(s => s.Conditions)
            .OrderBy(s => s.Order)
            .ToListAsync();

        stepsAfterResave.Should().HaveCount(2);
        stepsAfterResave[0].ActionType.Should().Be("send_message");
        stepsAfterResave[0].Order.Should().Be(0);
        stepsAfterResave[0].ParentStepId.Should().BeNull();
        stepsAfterResave[1].ActionType.Should().Be("timeout_user");
        stepsAfterResave[1].Order.Should().Be(1);
        stepsAfterResave[1].Conditions.Single().LeftOperand.Should().Be("role");
    }

    [Fact]
    public async Task Nested_if_pipeline_round_trips_through_BuildGraph_and_the_reverse_parse_path()
    {
        (PipelineService _, PipelineTestRunDbContext db) = Build();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new PipelineEntity
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "nested-if",
                TriggerKind = "command",
                IsEnabled = true,
            }
        );

        Guid ifStepId = Guid.NewGuid();
        Guid thenChildId = Guid.NewGuid();
        Guid elseChildId = Guid.NewGuid();

        db.PipelineSteps.AddRange(
            new PipelineStep
            {
                Id = ifStepId,
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                ParentStepId = null,
                Branch = null,
                BlockKind = "if",
                BlockConfigJson = "{\"ConditionRootId\":\"00000000-0000-0000-0000-000000000000\"}",
                Order = 0,
                ActionType = "block",
                ConfigJson = "{}",
            },
            new PipelineStep
            {
                Id = thenChildId,
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                ParentStepId = ifStepId,
                Branch = "then",
                BlockKind = null,
                Order = 0,
                ActionType = "send_message",
                ConfigJson = "{\"message\":\"then-branch\"}",
            },
            new PipelineStep
            {
                Id = elseChildId,
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                ParentStepId = ifStepId,
                Branch = "else",
                BlockKind = null,
                Order = 0,
                ActionType = "shoutout",
                ConfigJson = "{}",
            }
        );
        await db.SaveChangesAsync();

        List<PipelineStep> steps = await db
            .PipelineSteps.Where(s => s.PipelineId == pipelineId)
            .Include(s => s.Conditions)
            .ToListAsync();

        // ── Forward direction: BuildGraph must expose id/parent_step_id/branch/block_kind/
        // block_config/order for every step, resolvable back to the tree shape above.
        JsonElement graph = PipelineGraphBuilder.BuildGraph(steps);
        JsonElement wireSteps = graph.GetProperty("steps");
        wireSteps.GetArrayLength().Should().Be(3);

        JsonElement wireIf = wireSteps
            .EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == ifStepId.ToString());
        wireIf.GetProperty("parent_step_id").ValueKind.Should().Be(JsonValueKind.Null);
        wireIf.GetProperty("block_kind").GetString().Should().Be("if");
        wireIf
            .GetProperty("block_config")
            .GetProperty("ConditionRootId")
            .GetString()
            .Should()
            .Be("00000000-0000-0000-0000-000000000000");

        JsonElement wireThen = wireSteps
            .EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == thenChildId.ToString());
        wireThen.GetProperty("parent_step_id").GetString().Should().Be(ifStepId.ToString());
        wireThen.GetProperty("branch").GetString().Should().Be("then");
        wireThen.GetProperty("action").GetProperty("type").GetString().Should().Be("send_message");

        JsonElement wireElse = wireSteps
            .EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == elseChildId.ToString());
        wireElse.GetProperty("branch").GetString().Should().Be("else");
        wireElse.GetProperty("action").GetProperty("type").GetString().Should().Be("shoutout");

        // ── Reverse direction: feed the built graph back through the same save path a real
        // PUT would use (PipelineService is only usable here via a fresh Create so the sync
        // logic runs against a service instance bound to this db context).
        (PipelineService service2, PipelineTestRunDbContext _) = (
            new PipelineService(
                db,
                new PassThroughUnitOfWork(),
                Substitute.For<IEventBus>(),
                new CommandConfigValidator(
                    [
                        new FakeAction { ActionType = "send_message" },
                        new FakeAction { ActionType = "shoutout" },
                        new FakeAction { ActionType = "block" },
                    ],
                    new TemplateHelperValidator()
                ),
                Substitute.For<IChannelRegistry>()
            ),
            db
        );

        Result<PipelineDto> reparsed = await service2.UpdateAsync(
            Broadcaster.ToString(),
            pipelineId,
            new() { GraphJsonCache = graph }
        );
        reparsed.IsSuccess.Should().BeTrue(reparsed.ErrorMessage);

        List<PipelineStep> reloaded = await db
            .PipelineSteps.Where(s => s.PipelineId == pipelineId)
            .ToListAsync();
        reloaded.Should().HaveCount(3);

        PipelineStep reloadedIf = reloaded.Single(s => s.BlockKind == "if");
        reloadedIf.ParentStepId.Should().BeNull();
        reloadedIf.BlockConfigJson.Should().Contain("ConditionRootId");

        PipelineStep reloadedThen = reloaded.Single(s => s.Branch == "then");
        reloadedThen
            .ParentStepId.Should()
            .Be(
                reloadedIf.Id,
                "the then-child must still point at the SAME if-block after the round trip, even though every row got a fresh id"
            );
        reloadedThen.ActionType.Should().Be("send_message");

        PipelineStep reloadedElse = reloaded.Single(s => s.Branch == "else");
        reloadedElse.ParentStepId.Should().Be(reloadedIf.Id);
        reloadedElse.ActionType.Should().Be("shoutout");
    }
}
