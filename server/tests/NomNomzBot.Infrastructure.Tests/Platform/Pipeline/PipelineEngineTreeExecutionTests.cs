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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-c: <see cref="PipelineEngine"/> executing the DB-loaded PipelineStep TREE
/// (BlockKind/ParentStepId/Branch, condition-tree via PipelineStepCondition.ParentConditionId/GroupOp)
/// rather than a flat step list — pipeline-control-flow.md D1-D6, pipeline-tree-and-editor.md §1-2.
/// Runs on a real SQLite context (<see cref="PipelineTreeExecutionTestDbContext"/>): the engine's
/// retention sweep uses <c>ExecuteDeleteAsync</c>, unsupported on EF InMemory.
/// </summary>
public sealed class PipelineEngineTreeExecutionTests
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");

    // ─── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>Records every invocation's loop bindings so a test can assert exact iteration order —
    /// state the test observes, not "no exception".</summary>
    private sealed class RecordingLoopAction : ICommandAction
    {
        public string ActionType => "record_loop";
        public List<(string Index, string Item, string PreviousItem)> Invocations { get; } = [];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Invocations.Add(
                (
                    ctx.Variables.GetValueOrDefault("loop.index", ""),
                    ctx.Variables.GetValueOrDefault("loop.item", ""),
                    ctx.Variables.GetValueOrDefault("loop.previous_item", "")
                )
            );
            return Task.FromResult(ActionResult.Success());
        }
    }

    private sealed class AlwaysFailAction : ICommandAction
    {
        public string ActionType => "always_fail";

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Failure("boom"));
    }

    /// <summary>Fires <c>ctx.ShouldBreakLoop</c> only when <c>{{loop.index}}</c> equals the action's
    /// <c>"target"</c> config value — lets a test drive a break mid-loop without needing the (stubbed)
    /// template resolver to do real substitution.</summary>
    private sealed class ConditionalBreakAction : ICommandAction
    {
        public string ActionType => "break_at_index";

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            if (ctx.Variables.GetValueOrDefault("loop.index") == action.GetString("target"))
                ctx.ShouldBreakLoop = true;
            return Task.FromResult(ActionResult.Success());
        }
    }

    /// <summary>Fires <c>ctx.ShouldContinueLoop</c> only when <c>{{loop.index}}</c> equals the action's
    /// <c>"target"</c> config value — same rationale as <see cref="ConditionalBreakAction"/>.</summary>
    private sealed class ConditionalContinueAction : ICommandAction
    {
        public string ActionType => "continue_at_index";

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            if (ctx.Variables.GetValueOrDefault("loop.index") == action.GetString("target"))
                ctx.ShouldContinueLoop = true;
            return Task.FromResult(ActionResult.Success());
        }
    }

    /// <summary>Records how many times it ran — used to prove an outer loop kept iterating (or a
    /// step after a loop ran), independent of any inner loop's own <c>loop.*</c> bindings.</summary>
    private sealed class CountingAction : ICommandAction
    {
        public required string ActionType { get; init; }
        public int Count { get; private set; }

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Count++;
            return Task.FromResult(ActionResult.Success());
        }
    }

    /// <summary>Records the value of one named Run-scope variable each time it runs — used to prove
    /// what a callee actually saw (an arg) or what a caller actually saw after a sub-pipeline call
    /// (<c>{{call.result}}</c>), the state itself rather than "no exception".</summary>
    private sealed class RecordingValueAction : ICommandAction
    {
        public required string ActionType { get; init; }
        public required string VariableKey { get; init; }
        public List<string> Seen { get; } = [];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Seen.Add(ctx.Variables.GetValueOrDefault(VariableKey, "<missing>"));
            return Task.FromResult(ActionResult.Success());
        }
    }

    private static PipelineEngine CreateEngine(
        PipelineTreeExecutionTestDbContext db,
        IEnumerable<ICommandAction> extraActions,
        Func<double>? randomSource = null,
        ITemplateResolver? resolverOverride = null
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ITemplateResolver resolver = resolverOverride ?? Substitute.For<ITemplateResolver>();
        if (resolverOverride is null)
            resolver
                .ResolveAsync(
                    Arg.Any<string>(),
                    Arg.Any<IDictionary<string, string>>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(ci => Task.FromResult((string)ci[0]));

        List<ICommandAction> actions =
        [
            new StopAction(),
            new SetVariableAction(),
            new BreakAction(),
            new ContinueAction(),
            .. extraActions,
        ];
        ICommandCondition[] conditions =
        [
            new UserRoleCondition(),
            new ComparisonCondition(resolver),
        ];

        PipelineEngine engine = new(
            db,
            registry,
            actions,
            conditions,
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System,
            randomSource
        );

        // `actions` is the SAME list instance the engine holds a reference to (never copied in the
        // constructor), so it's safe to append these two after construction. RunPipelineAction takes
        // IServiceProvider (never IPipelineEngine directly — see its own doc comment on the real DI
        // circularity that would create); stub it to hand back this very engine.
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IPipelineEngine)).Returns(engine);
        actions.Add(new RunPipelineAction(serviceProvider, db));
        actions.Add(new ReturnValueAction());

        return engine;
    }

    private static PipelineRequest BuildRequest(
        Guid pipelineId,
        Dictionary<string, string>? initialVariables = null
    ) =>
        new()
        {
            BroadcasterId = TestChannel,
            PipelineId = pipelineId,
            TriggeredByUserId = "user1",
            TriggeredByDisplayName = "TestUser",
            MessageId = "msg1",
            RawMessage = "",
            InitialVariables = initialVariables ?? [],
        };

    // ─── Row builders ─────────────────────────────────────────────────────────

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
    ) =>
        NewStep(
            pipelineId,
            parentStepId,
            branch,
            blockKind: null,
            blockConfigJson: null,
            order,
            actionType,
            configJson
        );

    /// <summary>A minimal owned <see cref="NomNomzBot.Domain.Commands.Entities.Pipeline"/> row —
    /// <c>run_pipeline</c>'s tenant-scoping check (S-PIPE-TREE-d2) looks this up by id + BroadcasterId
    /// before ever loading the target's steps.</summary>
    private static NomNomzBot.Domain.Commands.Entities.Pipeline NewPipelineRow(
        Guid id,
        Guid broadcasterId,
        string name = "callee"
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcasterId,
            Name = name,
            TriggerKind = "manual",
        };

    private static PipelineStepCondition NewLeafCondition(
        Guid stepId,
        Guid? parentConditionId,
        int order,
        bool desiredResult,
        bool negate = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineStepId = stepId,
            BroadcasterId = TestChannel,
            ParentConditionId = parentConditionId,
            GroupOp = null,
            ConditionType = "comparison",
            Operator = "eq",
            LeftOperand = "1",
            RightOperand = desiredResult ? "1" : "2",
            Negate = negate,
            Order = order,
        };

    private static PipelineStepCondition NewGroup(
        Guid stepId,
        Guid? parentConditionId,
        string groupOp,
        int order,
        bool negate = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineStepId = stepId,
            BroadcasterId = TestChannel,
            ParentConditionId = parentConditionId,
            GroupOp = groupOp,
            ConditionType = "",
            Negate = negate,
            Order = order,
        };

    // ─── (A and B) or (C and not D) truth table ────────────────────────────────

    [Theory]
    [InlineData(true, true, false, false, true)] // A&&B
    [InlineData(false, false, true, false, true)] // C&&!D
    [InlineData(false, false, true, true, false)] // C&&D -> C&&!D fails
    [InlineData(true, false, false, false, false)] // A&&!B fails, C false
    [InlineData(false, false, false, false, false)] // everything false
    [InlineData(true, true, true, true, true)] // A&&B true regardless of C/D
    public async Task IfBlock_ConditionTree_RunsCorrectArmForFullTruthTable(
        bool a,
        bool b,
        bool c,
        bool d,
        bool expected
    )
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep ifStep = NewStep(
            pipelineId,
            parentStepId: null,
            branch: null,
            blockKind: "if",
            blockConfigJson: "{}",
            order: 0
        );
        PipelineStep thenLeaf = NewLeaf(
            pipelineId,
            ifStep.Id,
            "then",
            0,
            "set_variable",
            """{"type":"set_variable","name":"branch","value":"then"}"""
        );
        PipelineStep elseLeaf = NewLeaf(
            pipelineId,
            ifStep.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"branch","value":"else"}"""
        );

        // root: OR( AND(A,B), AND(C, NOT D) )
        PipelineStepCondition root = NewGroup(ifStep.Id, null, "or", 0);
        PipelineStepCondition andAB = NewGroup(ifStep.Id, root.Id, "and", 0);
        PipelineStepCondition leafA = NewLeafCondition(ifStep.Id, andAB.Id, 0, a);
        PipelineStepCondition leafB = NewLeafCondition(ifStep.Id, andAB.Id, 1, b);
        PipelineStepCondition andCNotD = NewGroup(ifStep.Id, root.Id, "and", 1);
        PipelineStepCondition leafC = NewLeafCondition(ifStep.Id, andCNotD.Id, 0, c);
        PipelineStepCondition leafD = NewLeafCondition(ifStep.Id, andCNotD.Id, 1, d, negate: true);

        db.PipelineSteps.AddRange(ifStep, thenLeaf, elseLeaf);
        db.PipelineStepConditions.AddRange(root, andAB, leafA, leafB, andCNotD, leafC, leafD);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.StepsExecuted.Should().Be(1);
        result.StepLogs.Should().ContainSingle();
        result.StepLogs[0].Output.Should().Be($"branch={(expected ? "then" : "else")}");
    }

    // ─── 3-levels-deep nesting: only the innermost matching arm runs ──────────

    [Fact]
    public async Task NestedIfBlocks_ThreeLevelsDeep_RunsOnlyInnermostArm_SiblingsNeverRun()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        List<PipelineStep> steps = [];
        List<PipelineStepCondition> conditions = [];

        PipelineStep BuildTrueIf(Guid? parentStepId, string? branch, int order)
        {
            PipelineStep ifStep = NewStep(pipelineId, parentStepId, branch, "if", "{}", order);
            PipelineStepCondition leaf = NewLeafCondition(ifStep.Id, null, 0, desiredResult: true);
            steps.Add(ifStep);
            conditions.Add(leaf);
            return ifStep;
        }

        PipelineStep level1 = BuildTrueIf(null, null, 0);
        PipelineStep level2 = BuildTrueIf(level1.Id, "then", 0);
        PipelineStep level3 = BuildTrueIf(level2.Id, "then", 0);

        PipelineStep innermostThen = NewLeaf(
            pipelineId,
            level3.Id,
            "then",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"innermost"}"""
        );
        // A sibling "else" leaf at EVERY level — must never execute.
        PipelineStep else1 = NewLeaf(
            pipelineId,
            level1.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"else1"}"""
        );
        PipelineStep else2 = NewLeaf(
            pipelineId,
            level2.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"else2"}"""
        );
        PipelineStep else3 = NewLeaf(
            pipelineId,
            level3.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"else3"}"""
        );

        db.PipelineSteps.AddRange([.. steps, innermostThen, else1, else2, else3]);
        db.PipelineStepConditions.AddRange(conditions);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.StepsExecuted.Should().Be(1);
        result.StepLogs.Should().ContainSingle();
        result.StepLogs[0].Output.Should().Be("marker=innermost");
    }

    // ─── switch / switch_case ───────────────────────────────────────────────

    [Fact]
    public async Task Switch_RunsExactlyOneMatchingCase()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep switchStep = NewStep(pipelineId, null, null, "switch", """{"value":"b"}""", 0);
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
        PipelineStep caseC = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"match":"c"}""",
            2
        );
        PipelineStep defaultCase = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"is_default":true}""",
            3
        );

        PipelineStep leafA = NewLeaf(
            pipelineId,
            caseA.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"a"}"""
        );
        PipelineStep leafB = NewLeaf(
            pipelineId,
            caseB.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"b"}"""
        );
        PipelineStep leafC = NewLeaf(
            pipelineId,
            caseC.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"c"}"""
        );
        PipelineStep leafDefault = NewLeaf(
            pipelineId,
            defaultCase.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"default"}"""
        );

        db.PipelineSteps.AddRange(
            switchStep,
            caseA,
            caseB,
            caseC,
            defaultCase,
            leafA,
            leafB,
            leafC,
            leafDefault
        );
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.StepsExecuted.Should().Be(1);
        result.StepLogs.Should().ContainSingle();
        result.StepLogs[0].Output.Should().Be("marker=b");
    }

    [Fact]
    public async Task Switch_NoCaseMatches_RunsDefault()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep switchStep = NewStep(
            pipelineId,
            null,
            null,
            "switch",
            """{"value":"zzz"}""",
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
        PipelineStep defaultCase = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"is_default":true}""",
            1
        );
        PipelineStep leafA = NewLeaf(
            pipelineId,
            caseA.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"a"}"""
        );
        PipelineStep leafDefault = NewLeaf(
            pipelineId,
            defaultCase.Id,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"default"}"""
        );

        db.PipelineSteps.AddRange(switchStep, caseA, defaultCase, leafA, leafDefault);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.StepLogs.Should().ContainSingle();
        result.StepLogs[0].Output.Should().Be("marker=default");
    }

    // ─── random_branch weighted selection (seeded — deterministic) ────────────

    [Fact]
    public async Task RandomBranch_WeightedCases_SelectsByWeightWithinTolerance()
    {
        // Weights 1/1/2 -> case Z (weight 2 of total 4) should be picked ~50% of the time.
        // A fixed-seed PRNG keeps this deterministic across runs (repo forbids nondeterminism in tests).
        Random seeded = new(1234);
        Dictionary<string, int> tally = new()
        {
            ["x"] = 0,
            ["y"] = 0,
            ["z"] = 0,
        };
        const int Runs = 300;

        for (int i = 0; i < Runs; i++)
        {
            using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
            Guid pipelineId = Guid.NewGuid();

            PipelineStep randomStep = NewStep(pipelineId, null, null, "random_branch", "{}", 0);
            PipelineStep caseX = NewStep(
                pipelineId,
                randomStep.Id,
                null,
                "random_case",
                """{"weight":1}""",
                0
            );
            PipelineStep caseY = NewStep(
                pipelineId,
                randomStep.Id,
                null,
                "random_case",
                """{"weight":1}""",
                1
            );
            PipelineStep caseZ = NewStep(
                pipelineId,
                randomStep.Id,
                null,
                "random_case",
                """{"weight":2}""",
                2
            );
            PipelineStep leafX = NewLeaf(
                pipelineId,
                caseX.Id,
                null,
                0,
                "set_variable",
                """{"type":"set_variable","name":"marker","value":"x"}"""
            );
            PipelineStep leafY = NewLeaf(
                pipelineId,
                caseY.Id,
                null,
                0,
                "set_variable",
                """{"type":"set_variable","name":"marker","value":"y"}"""
            );
            PipelineStep leafZ = NewLeaf(
                pipelineId,
                caseZ.Id,
                null,
                0,
                "set_variable",
                """{"type":"set_variable","name":"marker","value":"z"}"""
            );

            db.PipelineSteps.AddRange(randomStep, caseX, caseY, caseZ, leafX, leafY, leafZ);
            await db.SaveChangesAsync();

            PipelineEngine engine = CreateEngine(db, [], randomSource: seeded.NextDouble);
            PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

            string picked = result.StepLogs[0].Output!["marker=".Length..];
            tally[picked]++;
        }

        // Weight 2/4 = 50% expected for Z; allow generous tolerance for a 300-sample PRNG draw.
        double zFraction = tally["z"] / (double)Runs;
        zFraction.Should().BeInRange(0.35, 0.65);
        tally["x"].Should().BeGreaterThan(0);
        tally["y"].Should().BeGreaterThan(0);
    }

    // ─── loop: repeat / foreach ─────────────────────────────────────────────

    [Fact]
    public async Task Loop_Repeat_RunsChildrenExactlyNTimes()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":5}""",
            0
        );
        PipelineStep leaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            0,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );

        db.PipelineSteps.AddRange(loopStep, leaf);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result.StepsExecuted.Should().Be(5);
        recorder.Invocations.Should().HaveCount(5);
        recorder.Invocations.Select(i => i.Index).Should().Equal("0", "1", "2", "3", "4");
    }

    [Fact]
    public async Task Loop_ForEach_IteratesListBindingItemAndPreviousItem()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"foreach","list_var":"items"}""",
            0
        );
        PipelineStep leaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            0,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );

        db.PipelineSteps.AddRange(loopStep, leaf);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder]);
        PipelineExecutionResult result = await engine.ExecuteAsync(
            BuildRequest(pipelineId, new() { ["items"] = "x, y, z" })
        );

        result.StepsExecuted.Should().Be(3);
        recorder.Invocations.Select(i => i.Item).Should().Equal("x", "y", "z");
        recorder.Invocations.Select(i => i.Index).Should().Equal("0", "1", "2");
        recorder.Invocations.Select(i => i.PreviousItem).Should().Equal("", "x", "y");
    }

    // ─── runaway loop guard ─────────────────────────────────────────────────

    [Fact]
    public async Task Loop_RunawayWhileTrue_StopsAtCapWithRecordedReason()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        // while(true) with a tight per-block cap — proves the guard trips well before the engine's
        // 1000-iteration hard ceiling, and does so without hanging the test (a live-stream safety
        // property: a runaway loop must never wedge the bot mid-stream).
        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"while","max_iterations":20}""",
            0
        );
        PipelineStepCondition alwaysTrue = NewLeafCondition(
            loopStep.Id,
            null,
            0,
            desiredResult: true
        );
        PipelineStep leaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            0,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );

        db.PipelineSteps.AddRange(loopStep, leaf);
        db.PipelineStepConditions.Add(alwaysTrue);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.AbortedBudget);
        result.ErrorMessage.Should().Be("loop_iteration_cap_exceeded");
        recorder
            .Invocations.Should()
            .HaveCount(20, "the loop must stop exactly at its cap, not hang");
    }

    // ─── try / catch ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Try_FailingChildInsideTry_IsCaughtAndRunContinues()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep tryStep = NewStep(pipelineId, null, null, "try", "{}", 0);
        PipelineStep failingBody = NewLeaf(
            pipelineId,
            tryStep.Id,
            "then",
            0,
            "always_fail",
            """{"type":"always_fail"}"""
        );
        PipelineStep catchLeaf = NewLeaf(
            pipelineId,
            tryStep.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"caught"}"""
        );
        // A step after the try block must still run — the failure never aborted the whole run.
        PipelineStep afterTry = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"after"}"""
        );

        db.PipelineSteps.AddRange(tryStep, failingBody, catchLeaf, afterTry);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, [new AlwaysFailAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result.StepLogs.Should().HaveCount(3);
        result.StepLogs[0].Succeeded.Should().BeFalse();
        result.StepLogs[1].Output.Should().Be("marker=caught");
        result.StepLogs[2].Output.Should().Be("marker=after");
    }

    [Fact]
    public async Task SameFailure_OutsideTry_AbortsTheRun()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep failing = NewLeaf(
            pipelineId,
            null,
            null,
            0,
            "always_fail",
            """{"type":"always_fail"}"""
        );
        PipelineStep afterFailure = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"never"}"""
        );

        db.PipelineSteps.AddRange(failing, afterFailure);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, [new AlwaysFailAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        result
            .StepLogs.Should()
            .ContainSingle("the run breaks after the failing step, never reaching the second");
    }

    // ─── break / continue (S-PIPE-TREE-d1, pipeline-control-flow.md D3) ────────

    [Fact]
    public async Task Break_StopsFurtherIterations_StepsAfterLoopStillRun()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":5}""",
            0
        );
        PipelineStep recordLeaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            0,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );
        PipelineStep breakLeaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            1,
            "break_at_index",
            """{"type":"break_at_index","target":"2"}"""
        );
        PipelineStep afterLoop = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"after"}"""
        );

        db.PipelineSteps.AddRange(loopStep, recordLeaf, breakLeaf, afterLoop);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder, new ConditionalBreakAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        // The loop body still runs index 2 (break fires AFTER the record leaf) then stops — index 3
        // and 4 never run.
        recorder.Invocations.Select(i => i.Index).Should().Equal("0", "1", "2");
        result
            .StepLogs[^1]
            .Output.Should()
            .Be("marker=after", "a step after the loop must still run");
    }

    [Fact]
    public async Task Continue_SkipsRemainingStepsOfThatIterationOnly_NextIterationRuns()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":3}""",
            0
        );
        // continue check runs FIRST in the body — proves it skips the record leaf that follows it,
        // for that iteration only.
        PipelineStep continueLeaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            0,
            "continue_at_index",
            """{"type":"continue_at_index","target":"1"}"""
        );
        PipelineStep recordLeaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            1,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );

        db.PipelineSteps.AddRange(loopStep, continueLeaf, recordLeaf);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder, new ConditionalContinueAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        // Index 1's record leaf was skipped by continue, but iterations 0 and 2 both ran normally.
        recorder.Invocations.Select(i => i.Index).Should().Equal("0", "2");
    }

    [Fact]
    public async Task NestedLoop_InnerBreak_LeavesOuterLoopStillIterating()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep outerLoop = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":3}""",
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
        PipelineStep innerRecordLeaf = NewLeaf(
            pipelineId,
            innerLoop.Id,
            null,
            0,
            "record_inner",
            "{\"type\":\"record_inner\"}"
        );
        PipelineStep innerBreakLeaf = NewLeaf(
            pipelineId,
            innerLoop.Id,
            null,
            1,
            "break_at_index",
            """{"type":"break_at_index","target":"1"}"""
        );
        // Runs AFTER the inner loop finishes, once per outer iteration — proves the outer loop kept
        // going despite the inner loop breaking early every single time.
        PipelineStep outerRecordLeaf = NewLeaf(
            pipelineId,
            outerLoop.Id,
            null,
            1,
            "record_outer",
            "{\"type\":\"record_outer\"}"
        );

        db.PipelineSteps.AddRange(
            outerLoop,
            innerLoop,
            innerRecordLeaf,
            innerBreakLeaf,
            outerRecordLeaf
        );
        await db.SaveChangesAsync();

        CountingAction innerRecorder = new() { ActionType = "record_inner" };
        CountingAction outerRecorder = new() { ActionType = "record_outer" };
        PipelineEngine engine = CreateEngine(
            db,
            [innerRecorder, outerRecorder, new ConditionalBreakAction()]
        );
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        outerRecorder.Count.Should().Be(3, "the outer loop must complete all 3 iterations");
        innerRecorder
            .Count.Should()
            .Be(
                6,
                "each of the 3 outer passes re-runs the inner loop, which records index 0 and 1 (2x) before breaking at index 1"
            );
    }

    [Fact]
    public async Task Break_InsideTryInsideLoop_ExitsLoop_IsNotTreatedAsCaughtError()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":5}""",
            0
        );
        PipelineStep tryStep = NewStep(pipelineId, loopStep.Id, null, "try", "{}", 0);
        PipelineStep recordLeaf = NewLeaf(
            pipelineId,
            tryStep.Id,
            "then",
            0,
            "record_loop",
            "{\"type\":\"record_loop\"}"
        );
        PipelineStep breakLeaf = NewLeaf(
            pipelineId,
            tryStep.Id,
            "then",
            1,
            "break_at_index",
            """{"type":"break_at_index","target":"1"}"""
        );
        // The catch arm must NEVER run — a break is not a caught failure.
        PipelineStep catchLeaf = NewLeaf(
            pipelineId,
            tryStep.Id,
            "else",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"caught"}"""
        );

        db.PipelineSteps.AddRange(loopStep, tryStep, recordLeaf, breakLeaf, catchLeaf);
        await db.SaveChangesAsync();

        RecordingLoopAction recorder = new();
        PipelineEngine engine = CreateEngine(db, [recorder, new ConditionalBreakAction()]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result
            .Outcome.Should()
            .Be(PipelineOutcome.Completed, "a break is deliberate control flow, never a failure");
        recorder.Invocations.Select(i => i.Index).Should().Equal("0", "1");
        result
            .StepLogs.Should()
            .NotContain(
                l => l.Output == "marker=caught",
                "the catch arm must never run for a break"
            );
    }

    [Fact]
    public async Task Break_OutsideAnyLoop_IsAnHonestNoOp_SubsequentStepsStillRunAndOutcomeIsCompleted()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep breakLeaf = NewLeaf(
            pipelineId,
            null,
            null,
            0,
            "break",
            """{"type":"break"}"""
        );
        PipelineStep afterLeaf = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"after"}"""
        );

        db.PipelineSteps.AddRange(breakLeaf, afterLeaf);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result
            .Outcome.Should()
            .Be(
                PipelineOutcome.Completed,
                "no enclosing loop to break — an honest no-op, never a silent abort"
            );
        result.StepLogs.Should().HaveCount(2);
        result.StepLogs[0].Succeeded.Should().BeTrue();
        result
            .StepLogs[1]
            .Output.Should()
            .Be("marker=after", "the step after the no-op break still runs");
    }

    [Fact]
    public async Task Continue_OutsideAnyLoop_IsAnHonestNoOp_SubsequentStepsStillRunAndOutcomeIsCompleted()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep continueLeaf = NewLeaf(
            pipelineId,
            null,
            null,
            0,
            "continue",
            """{"type":"continue"}"""
        );
        PipelineStep afterLeaf = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"after"}"""
        );

        db.PipelineSteps.AddRange(continueLeaf, afterLeaf);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result
            .Outcome.Should()
            .Be(
                PipelineOutcome.Completed,
                "no enclosing loop to continue — an honest no-op, never a silent abort"
            );
        result.StepLogs.Should().HaveCount(2);
        result.StepLogs[0].Succeeded.Should().BeTrue();
        result
            .StepLogs[1]
            .Output.Should()
            .Be("marker=after", "the step after the no-op continue still runs");
    }

    // ─── recursion depth ──────────────────────────────────────────────────────

    [Fact]
    public async Task NestedIfChain_BeyondMaxRecursionDepth_AbortsCleanly_LeafNeverRuns()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        List<PipelineStep> steps = [];
        List<PipelineStepCondition> conditions = [];

        Guid? parentId = null;
        string? branch = null;
        PipelineStep last = null!;
        // 9 nested if-blocks: the engine caps block-nesting depth at 8, so the 9th's arm must never run.
        for (int level = 0; level < 9; level++)
        {
            PipelineStep ifStep = NewStep(pipelineId, parentId, branch, "if", "{}", 0);
            PipelineStepCondition leaf = NewLeafCondition(ifStep.Id, null, 0, desiredResult: true);
            steps.Add(ifStep);
            conditions.Add(leaf);
            parentId = ifStep.Id;
            branch = "then";
            last = ifStep;
        }

        PipelineStep innermostLeaf = NewLeaf(
            pipelineId,
            last.Id,
            "then",
            0,
            "set_variable",
            """{"type":"set_variable","name":"marker","value":"unreachable"}"""
        );
        steps.Add(innermostLeaf);

        db.PipelineSteps.AddRange(steps);
        db.PipelineStepConditions.AddRange(conditions);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.AbortedBudget);
        result.ErrorMessage.Should().Be("max_recursion_depth_exceeded");
        result
            .StepsExecuted.Should()
            .Be(0, "the innermost leaf must never run — a clean abort, not a crash");
    }

    // ─── flat DB pipelines: zero behaviour change ──────────────────────────────

    [Fact]
    public async Task FlatDbPipeline_NoBlockKindNoParent_ExecutesLikeBeforeViaOriginalFlatPath()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep first = NewLeaf(
            pipelineId,
            null,
            null,
            0,
            "set_variable",
            """{"type":"set_variable","name":"x","value":"1"}"""
        );
        PipelineStep second = NewLeaf(pipelineId, null, null, 1, "stop", """{"type":"stop"}""");

        db.PipelineSteps.AddRange(first, second);
        await db.SaveChangesAsync();

        PipelineEngine engine = CreateEngine(db, []);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        // Same outcome shape as the JSON-path equivalent (ExecuteAsync_SetVariable_StoresInContext):
        // a trailing deliberate `stop` reports Stopped, both steps logged.
        result.Outcome.Should().Be(PipelineOutcome.Stopped);
        result.StepLogs.Should().HaveCount(2);
        result.Total.Should().Be(2);
    }

    /// <summary>Adversarial case the original break/continue slice did not cover: a <c>break</c> inside a
    /// <c>switch</c> that is itself inside a <c>loop</c>. In many engines break is captured by the nearest
    /// enclosing construct, so it would exit only the SWITCH and let the loop keep iterating — the existing
    /// tests would still pass while the behaviour is wrong. Here the loop must stop.</summary>
    [Fact]
    public async Task Break_InsideSwitchInsideLoop_ExitsTheLoop_NotJustTheSwitch()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep loopStep = NewStep(
            pipelineId,
            null,
            null,
            "loop",
            """{"mode":"repeat","count":5}""",
            0
        );
        PipelineStep switchStep = NewStep(
            pipelineId,
            loopStep.Id,
            null,
            "switch",
            """{"value":"go"}""",
            0
        );
        PipelineStep caseStep = NewStep(
            pipelineId,
            switchStep.Id,
            null,
            "switch_case",
            """{"match":"go"}""",
            0
        );
        PipelineStep recordLeaf = NewLeaf(
            pipelineId,
            caseStep.Id,
            null,
            0,
            "record_inner",
            "{\"type\":\"record_inner\"}"
        );
        PipelineStep breakLeaf = NewLeaf(
            pipelineId,
            caseStep.Id,
            null,
            1,
            "break_at_index",
            """{"type":"break_at_index","target":"0"}"""
        );
        // Sits AFTER the switch inside the loop body: if break only escaped the switch, this would still
        // run on every iteration and the loop would complete all 5 passes.
        PipelineStep afterSwitchLeaf = NewLeaf(
            pipelineId,
            loopStep.Id,
            null,
            1,
            "record_outer",
            "{\"type\":\"record_outer\"}"
        );

        db.PipelineSteps.AddRange(
            loopStep,
            switchStep,
            caseStep,
            recordLeaf,
            breakLeaf,
            afterSwitchLeaf
        );
        await db.SaveChangesAsync();

        CountingAction innerRecorder = new() { ActionType = "record_inner" };
        CountingAction afterRecorder = new() { ActionType = "record_outer" };
        PipelineEngine engine = CreateEngine(
            db,
            [innerRecorder, afterRecorder, new ConditionalBreakAction()]
        );
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        innerRecorder
            .Count.Should()
            .Be(1, "the switch case body runs once, on the only iteration before the break");
        afterRecorder
            .Count.Should()
            .Be(
                0,
                "break must exit the LOOP, so the step after the switch never runs; if break only escaped the switch this would be 5"
            );
    }

    // ─── S-PIPE-TREE-d2: run_pipeline (inline/detached) + return_value ────────

    [Fact]
    public async Task RunPipelineInline_PassesArgs_CalleeReturnsValue_CallerUsesItNextStep()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid callerPipelineId = Guid.NewGuid();
        Guid calleePipelineId = Guid.NewGuid();

        db.Pipelines.Add(NewPipelineRow(calleePipelineId, TestChannel));

        // Callee: records the arg it was called with, then returns "42".
        PipelineStep calleeRecordArg = NewLeaf(
            calleePipelineId,
            null,
            null,
            0,
            "record_arg",
            """{"type":"record_arg"}"""
        );
        PipelineStep calleeReturn = NewLeaf(
            calleePipelineId,
            null,
            null,
            1,
            "return_value",
            """{"type":"return_value","value":"42"}"""
        );

        // Caller: run_pipeline inline (with one arg), then a step that reads {{call.result}}.
        PipelineStep callerRunPipeline = NewLeaf(
            callerPipelineId,
            null,
            null,
            0,
            "run_pipeline",
            $$"""{"type":"run_pipeline","pipeline":"{{calleePipelineId}}","mode":"inline","args":["hello"]}"""
        );
        PipelineStep callerRecordResult = NewLeaf(
            callerPipelineId,
            null,
            null,
            1,
            "record_result",
            """{"type":"record_result"}"""
        );

        db.PipelineSteps.AddRange(
            calleeRecordArg,
            calleeReturn,
            callerRunPipeline,
            callerRecordResult
        );
        await db.SaveChangesAsync();

        RecordingValueAction argRecorder = new()
        {
            ActionType = "record_arg",
            VariableKey = "args.1",
        };
        RecordingValueAction resultRecorder = new()
        {
            ActionType = "record_result",
            VariableKey = "call.result",
        };
        PipelineEngine engine = CreateEngine(db, [argRecorder, resultRecorder]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(callerPipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        argRecorder.Seen.Should().Equal("hello");
        resultRecorder.Seen.Should().Equal("42");
    }

    [Fact]
    public async Task RunPipelineInline_CrossChannelTarget_FailsAndNeverRunsTheOtherChannelsSteps()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid otherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
        Guid callerPipelineId = Guid.NewGuid();
        Guid foreignPipelineId = Guid.NewGuid();

        // Owned by a DIFFERENT channel than the caller.
        db.Pipelines.Add(NewPipelineRow(foreignPipelineId, otherChannel));

        PipelineStep foreignLeaf = NewLeaf(
            foreignPipelineId,
            null,
            null,
            0,
            "record_foreign",
            """{"type":"record_foreign"}"""
        );
        PipelineStep callerRunPipeline = NewLeaf(
            callerPipelineId,
            null,
            null,
            0,
            "run_pipeline",
            $$"""{"type":"run_pipeline","pipeline":"{{foreignPipelineId}}","mode":"inline"}"""
        );

        db.PipelineSteps.AddRange(foreignLeaf, callerRunPipeline);
        await db.SaveChangesAsync();

        CountingAction foreignRecorder = new() { ActionType = "record_foreign" };
        PipelineEngine engine = CreateEngine(db, [foreignRecorder]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(callerPipelineId));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        foreignRecorder.Count.Should().Be(0, "none of the other channel's steps may execute");
    }

    [Fact]
    public async Task RunPipelineInline_SelfRecursion_AbortsAtMaxRecursionDepth_InnermostNeverRuns()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        db.Pipelines.Add(NewPipelineRow(pipelineId, TestChannel));

        // A pipeline that calls ITSELF inline every time it runs — the classic unbounded-cycle shape
        // control-flow D4 exists to bound. Every level records before recursing, so the recorder's
        // count is exactly how many levels the engine actually entered.
        PipelineStep countStep = NewLeaf(
            pipelineId,
            null,
            null,
            0,
            "count_calls",
            """{"type":"count_calls"}"""
        );
        PipelineStep recurseStep = NewLeaf(
            pipelineId,
            null,
            null,
            1,
            "run_pipeline",
            $$"""{"type":"run_pipeline","pipeline":"{{pipelineId}}","mode":"inline"}"""
        );

        db.PipelineSteps.AddRange(countStep, recurseStep);
        await db.SaveChangesAsync();

        CountingAction counter = new() { ActionType = "count_calls" };
        PipelineEngine engine = CreateEngine(db, [counter]);

        // Proof the run terminates at all (never hangs, never stack-overflows the test process) is
        // that this call returns within the test's own timeout.
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        counter
            .Count.Should()
            .Be(
                9,
                "the top-level run plus 8 nested inline calls (CallDepth 0..7 all proceed) is exactly "
                    + "MaxRecursionDepth=8 levels of actual work; the 9th attempted call is rejected "
                    + "before its own body — including its own count_calls leaf — ever runs"
            );
    }

    [Fact]
    public async Task RunPipelineInline_FailingCallee_CaughtByTryAtCallSite()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid callerPipelineId = Guid.NewGuid();
        Guid calleePipelineId = Guid.NewGuid();

        db.Pipelines.Add(NewPipelineRow(calleePipelineId, TestChannel));

        PipelineStep calleeFailingLeaf = NewLeaf(
            calleePipelineId,
            null,
            null,
            0,
            "always_fail",
            """{"type":"always_fail"}"""
        );

        PipelineStep tryStep = NewStep(callerPipelineId, null, null, "try", "{}", 0);
        PipelineStep tryRunPipeline = NewLeaf(
            callerPipelineId,
            tryStep.Id,
            "then",
            0,
            "run_pipeline",
            $$"""{"type":"run_pipeline","pipeline":"{{calleePipelineId}}","mode":"inline"}"""
        );
        PipelineStep catchMarker = NewLeaf(
            callerPipelineId,
            tryStep.Id,
            "else",
            0,
            "caught_marker",
            """{"type":"caught_marker"}"""
        );

        db.PipelineSteps.AddRange(calleeFailingLeaf, tryStep, tryRunPipeline, catchMarker);
        await db.SaveChangesAsync();

        CountingAction caughtMarker = new() { ActionType = "caught_marker" };
        PipelineEngine engine = CreateEngine(db, [new AlwaysFailAction(), caughtMarker]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(callerPipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        caughtMarker.Count.Should().Be(1, "the try's catch arm must run once the callee failed");
    }
}
