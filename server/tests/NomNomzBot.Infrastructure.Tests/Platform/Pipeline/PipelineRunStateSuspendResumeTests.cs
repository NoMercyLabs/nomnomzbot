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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d3a: persistence + suspend/resume CORE for a tree-shaped pipeline run.
/// <see cref="PipelineRunState"/> survives a simulated process restart (a brand-new
/// <see cref="PipelineEngine"/> instance re-reads the SAME persisted row via
/// <see cref="PipelineEngine.ResumeAsync"/>) and resumes at the exact next step with its variable bag
/// and loop/switch cursors intact. Does NOT cover event-matching or timeout policy — that is d3b.
/// </summary>
public sealed class PipelineRunStateSuspendResumeTests
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>Appends the CURRENT <c>loop.index</c> to a shared list every time it runs — the shared
    /// list is passed BY REFERENCE across two separate engine instances (simulating a real durable side
    /// effect that survives past a single in-memory run), so the test can assert exactly what ran before
    /// suspension vs. after resume, with no duplicates and no gaps.</summary>
    private sealed class RecordAction(List<string> sink) : ICommandAction
    {
        public string ActionType => "record";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            sink.Add(ctx.Variables.GetValueOrDefault("loop.index", "-"));
            return Task.FromResult(ActionResult.Success());
        }
    }

    /// <summary>Suspends the run exactly when <c>loop.index</c> equals the step's configured
    /// <c>"target"</c> — deciding inside the action, not via a pipeline condition, so the test never
    /// depends on template resolution.</summary>
    private sealed class ConditionalSuspendAction : ICommandAction
    {
        public string ActionType => "conditional_suspend";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) =>
            Task.FromResult(
                ctx.Variables.GetValueOrDefault("loop.index") == action.GetString("target")
                    ? ActionResult.Suspend()
                    : ActionResult.Success()
            );
    }

    /// <summary>Always suspends — used inside a switch case where there is no loop index to gate on.</summary>
    private sealed class AlwaysSuspendAction : ICommandAction
    {
        public string ActionType => "always_suspend";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Suspend());
    }

    private static PipelineEngine CreateEngine(
        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db,
        IEnumerable<ICommandAction> extraActions,
        TimeProvider? timeProvider = null
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        List<ICommandAction> actions = [new StopAction(), new SetVariableAction(), .. extraActions];
        ICommandCondition[] conditions = [];

        return new(
            db,
            registry,
            actions,
            conditions,
            resolver,
            NullLogger<PipelineEngine>.Instance,
            timeProvider ?? TimeProvider.System
        );
    }

    private static PipelineRequest BuildRequest(
        Guid pipelineId,
        Dictionary<string, string>? initialVariables = null
    ) =>
        new()
        {
            BroadcasterId = TestChannel,
            PipelineId = pipelineId,
            TriggeredByUserId = "0192a000-0000-7000-8000-00000000abcd",
            TriggeredByDisplayName = "TestUser",
            MessageId = "msg1",
            RawMessage = "",
            InitialVariables = initialVariables ?? [],
        };

    private static PipelineStep NewStep(
        Guid pipelineId,
        Guid? parentStepId,
        string? branch,
        string? blockKind,
        string? blockConfigJson,
        int order,
        string actionType = "noop",
        string configJson = """{"type":"noop"}"""
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = TestChannel,
            ParentStepId = parentStepId,
            Branch = branch,
            BlockKind = blockKind,
            BlockConfigJson = blockConfigJson,
            Order = order,
            ActionType = actionType,
            ConfigJson = configJson,
            IsEnabled = true,
        };

    private static PipelineStep NewLeaf(
        Guid pipelineId,
        Guid? parentStepId,
        string? branch,
        int order,
        string actionType,
        string configJson
    ) => NewStep(pipelineId, parentStepId, branch, null, null, order, actionType, configJson);

    // ─── Nested-loop suspend/resume ────────────────────────────────────────────

    [Fact]
    public async Task Resume_SuspendedInsideNestedLoop_ContinuesAtExactIterationWithNoDuplicateOrSkippedWork()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep outerLoop = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":1}""",
            0
        );
        PipelineStep innerLoop = NewStep(
            pipelineId,
            outerLoop.Id,
            null,
            "loop",
            """{"mode":"repeat","count":3}""",
            0
        );
        PipelineStep recordLeaf = NewLeaf(
            pipelineId,
            innerLoop.Id,
            null,
            0,
            "record",
            """{"type":"record"}"""
        );
        PipelineStep suspendLeaf = NewLeaf(
            pipelineId,
            innerLoop.Id,
            null,
            1,
            "conditional_suspend",
            """{"type":"conditional_suspend","target":"1"}"""
        );

        db.PipelineSteps.AddRange(outerLoop, innerLoop, recordLeaf, suspendLeaf);
        await db.SaveChangesAsync();

        List<string> recorded = [];

        // ─── Segment 1: run until it suspends ───
        PipelineEngine engine1 = CreateEngine(
            db,
            [new RecordAction(recorded), new ConditionalSuspendAction()]
        );
        PipelineExecutionResult first = await engine1.ExecuteAsync(BuildRequest(pipelineId));

        first.Outcome.Should().Be(PipelineOutcome.Suspended);
        first.SuspendedRunStateId.Should().NotBeNull();
        recorded.Should().Equal("0", "1"); // inner index 0 recorded, then index 1 recorded, THEN suspended

        PipelineRunState persisted = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        persisted.Status.Should().Be("suspended");
        persisted.SuspendedAtStepId.Should().Be(suspendLeaf.Id);
        persisted.CursorJson.Should().Contain(outerLoop.Id.ToString());
        persisted.CursorJson.Should().Contain(innerLoop.Id.ToString());

        // ─── Simulate a process restart: a BRAND NEW engine instance, over the SAME persisted row ───
        PipelineEngine engine2 = CreateEngine(
            db,
            [new RecordAction(recorded), new ConditionalSuspendAction()]
        );
        PipelineExecutionResult second = await engine2.ResumeAsync(
            first.SuspendedRunStateId!.Value
        );

        second.Outcome.Should().Be(PipelineOutcome.Completed);
        // No duplicate of "1" (the suspended leaf's own record already ran) and no gap before "2".
        recorded.Should().Equal("0", "1", "2");

        PipelineRunState completed = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        completed.Status.Should().Be("completed");
        completed.CompletedAt.Should().NotBeNull();
    }

    // ─── Switch-arm suspend/resume ─────────────────────────────────────────────

    [Fact]
    public async Task Resume_SuspendedInsideSwitchArm_ReentersTheSameCaseWithoutReevaluatingAndSkipsTheRanLeaf()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep switchStep = NewStep(
            pipelineId,
            null,
            null,
            "switch",
            """{"value":"sw"}""",
            0
        );
        PipelineStep caseA = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"match":"a"}""",
            0
        );
        PipelineStep caseB = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"match":"b"}""",
            1
        );
        PipelineStep recordA = NewLeaf(
            pipelineId,
            caseA.Id,
            null,
            0,
            "record",
            """{"type":"record"}"""
        );
        PipelineStep recordB1 = NewLeaf(
            pipelineId,
            caseB.Id,
            null,
            0,
            "record",
            """{"type":"record"}"""
        );
        PipelineStep suspendB = NewLeaf(
            pipelineId,
            caseB.Id,
            null,
            1,
            "always_suspend",
            """{"type":"always_suspend"}"""
        );
        PipelineStep recordB2 = NewLeaf(
            pipelineId,
            caseB.Id,
            null,
            2,
            "record",
            """{"type":"record"}"""
        );

        db.PipelineSteps.AddRange(switchStep, caseA, caseB, recordA, recordB1, suspendB, recordB2);
        await db.SaveChangesAsync();

        List<string> recorded = [];

        PipelineEngine engine1 = CreateEngine(
            db,
            [new RecordAction(recorded), new AlwaysSuspendAction()]
        );
        PipelineExecutionResult first = await engine1.ExecuteAsync(
            BuildRequest(pipelineId, new() { ["sw"] = "b" })
        );

        first.Outcome.Should().Be(PipelineOutcome.Suspended);
        first.SuspendedAtStepId.Should().Be(suspendB.Id);
        recorded.Should().Equal("-"); // recordB1 only — no loop, so RecordAction's binding is "-"

        // Restart: fresh engine, and note InitialVariables is NEVER passed to ResumeAsync — the switch
        // value must come from the PERSISTED variable bag, proving VariablesJson round-trips correctly.
        PipelineEngine engine2 = CreateEngine(
            db,
            [new RecordAction(recorded), new AlwaysSuspendAction()]
        );
        PipelineExecutionResult second = await engine2.ResumeAsync(
            first.SuspendedRunStateId!.Value
        );

        second.Outcome.Should().Be(PipelineOutcome.Completed);
        // recordA never ran (would have appended a second "-" too, but the count check below still
        // proves it): only recordB1 (pre-suspend) + recordB2 (post-resume) — never re-entered case A,
        // never re-ran the suspended leaf itself.
        recorded.Should().Equal("-", "-");
    }

    // ─── Per-channel suspended-run cap ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuspendedRunCapReached_FailsHonestlyInsteadOfSilentlyDroppingTheRun()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        // A trivial wrapping block is required so the engine routes this pipeline through the TREE
        // walker (which is what understands ActionResult.Suspended) rather than the flat path — a
        // single top-level leaf with no BlockKind/ParentStepId would be (correctly) treated as a flat
        // pipeline, which never suspends at all (S-PIPE-TREE-d3a's unchanged-flat-path guarantee).
        PipelineStep wrapperLoop = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":1}""",
            0
        );
        PipelineStep suspendLeaf = NewLeaf(
            pipelineId,
            wrapperLoop.Id,
            null,
            0,
            "always_suspend",
            """{"type":"always_suspend"}"""
        );
        db.PipelineSteps.AddRange(wrapperLoop, suspendLeaf);

        // Pre-fill the cap (50) with unrelated already-suspended rows for the SAME channel.
        for (int i = 0; i < 50; i++)
        {
            db.PipelineRunStates.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = TestChannel,
                    PipelineId = pipelineId,
                    Status = "suspended",
                    VariablesJson = "{}",
                    CursorJson = "[]",
                    TriggeredByUserId = Guid.NewGuid(),
                    TriggeredByDisplayName = "filler",
                }
            );
        }
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, [new AlwaysSuspendAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Failed);
        result.ErrorMessage.Should().Contain("suspended_run_cap_exceeded");

        // The would-be 51st row must never have been persisted — the cap fails the run, it does not
        // silently drop it (no orphaned suspended state), and the pre-existing 50 stay untouched.
        int suspendedCount = await db.PipelineRunStates.CountAsync(r =>
            r.BroadcasterId == TestChannel && r.Status == "suspended"
        );
        suspendedCount.Should().Be(50);
    }

    // ─── MaxRuntime excludes suspended wall-clock ──────────────────────────────

    [Fact]
    public async Task ResumeAsync_LongSuspendedInterval_DoesNotCountTowardMaxRuntime()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep leaf = NewLeaf(pipelineId, null, null, 0, "record", """{"type":"record"}""");
        db.PipelineSteps.Add(leaf);

        Guid runStateId = Guid.NewGuid();
        db.PipelineRunStates.Add(
            new()
            {
                Id = runStateId,
                BroadcasterId = TestChannel,
                PipelineId = pipelineId,
                Status = "suspended",
                SuspendedAtStepId = null, // resumes at the top — cursor path is empty
                VariablesJson = "{}",
                CursorJson = "[]",
                TriggeredByUserId = Guid.NewGuid(),
                TriggeredByDisplayName = "TestUser",
                // Well under the 5-minute budget — only the ACTUAL prior run time, never the parked
                // interval, counts toward it.
                AccumulatedRuntimeMs = 1000,
                SuspendedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        // Fake clock jumps forward 2 REAL hours between suspend and resume — a run parked that long has
        // not "run" for two hours (settled CTO decision). Only wall-clock actually spent executing counts.
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordAction(recorded)], clock);
        clock.Advance(TimeSpan.FromHours(2));

        PipelineExecutionResult result = await engine.ResumeAsync(runStateId);

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        recorded.Should().Equal("-");
    }

    [Fact]
    public async Task ResumeAsync_AccumulatedRuntimeAlreadyAtBudget_TimesOutImmediately()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep leaf = NewLeaf(pipelineId, null, null, 0, "record", """{"type":"record"}""");
        db.PipelineSteps.Add(leaf);

        Guid runStateId = Guid.NewGuid();
        db.PipelineRunStates.Add(
            new()
            {
                Id = runStateId,
                BroadcasterId = TestChannel,
                PipelineId = pipelineId,
                Status = "suspended",
                VariablesJson = "{}",
                CursorJson = "[]",
                TriggeredByUserId = Guid.NewGuid(),
                TriggeredByDisplayName = "TestUser",
                // At/over the 5-minute ExecutionTimeout purely from ACTUAL prior running time — the
                // guard must still stop a genuinely runaway run even though it is being resumed.
                AccumulatedRuntimeMs = (int)TimeSpan.FromMinutes(5).TotalMilliseconds,
            }
        );
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordAction(recorded)]);
        PipelineExecutionResult result = await engine.ResumeAsync(runStateId);

        result.Outcome.Should().Be(PipelineOutcome.TimedOut);
        recorded.Should().BeEmpty(); // never touched a single step
    }

    // ─── Flat-pipeline path is untouched ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FlatPipeline_StillRunsThroughTheOriginalNonTreePath()
    {
        // A flat pipeline (every row a top-level leaf, no BlockKind/ParentStepId) must still resolve
        // isTreeRun=false and run via RunStepsAsync — completely unaffected by this slice's tree-walker
        // surgery (S-PIPE-TREE-d3a's hard safety property).
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep leaf = NewLeaf(pipelineId, null, null, 0, "record", """{"type":"record"}""");
        db.PipelineSteps.Add(leaf);
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordAction(recorded)]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        recorded.Should().Equal("-");
    }
}
