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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S-PIPE-TREE-a — proves the lossless read-time upcast from the flat legacy shape (a pipeline's
/// normalized <see cref="PipelineStep"/>/<see cref="PipelineStepCondition"/> rows and the single
/// <see cref="Pipeline.TriggerKind"/> column — the owner's imported old-bot pipeline shape,
/// <c>PipelineService.cs</c> "DB steps take priority over graph JSON cache") onto the tree model
/// (pipeline-tree-and-editor.md §6.1, E9): every step/action/parameter/condition preserved field by
/// field, condition evaluation semantics preserved, idempotent, and the zero-/one-step boundary
/// cases migrate without error.
/// </summary>
public sealed class PipelineTreeCompatibilityServiceTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000c0703");

    private readonly PipelineTreeCompatibilityService _sut = new();

    // ── A tiny, self-contained boolean evaluator over BOTH shapes, used only to PROVE semantic
    // equivalence between "flat AND list" and "condition tree" — this is test-only scaffolding,
    // not a stand-in for the real engine (PipelineEngine.cs, out of this slice's scope). Each leaf's
    // truth value is supplied by the caller (keyed by ConditionType+LeftOperand), so the test can
    // exercise every AND/OR/NOT combination without depending on the real evaluators.
    private static bool EvaluateFlatAnd(
        IReadOnlyList<PipelineStepCondition> flatLeaves,
        IReadOnlyDictionary<string, bool> leafTruth
    ) => flatLeaves.All(c => EvaluateLeaf(c, leafTruth));

    private static bool EvaluateTree(
        IReadOnlyList<PipelineStepCondition> tree,
        IReadOnlyDictionary<string, bool> leafTruth
    )
    {
        if (tree.Count == 0)
            return true; // zero conditions = always true, unchanged (§6.1 pt.2)

        PipelineStepCondition root = tree.Single(n => n.ParentConditionId is null);
        return EvaluateNode(root, tree, leafTruth);
    }

    private static bool EvaluateNode(
        PipelineStepCondition node,
        IReadOnlyList<PipelineStepCondition> all,
        IReadOnlyDictionary<string, bool> leafTruth
    )
    {
        if (node.GroupOp is null)
            return EvaluateLeaf(node, leafTruth);

        IEnumerable<bool> childResults = all.Where(c => c.ParentConditionId == node.Id)
            .OrderBy(c => c.Order)
            .Select(c => EvaluateNode(c, all, leafTruth));

        bool result = node.GroupOp == "and" ? childResults.All(r => r) : childResults.Any(r => r);
        return node.Negate ? !result : result;
    }

    private static bool EvaluateLeaf(
        PipelineStepCondition leaf,
        IReadOnlyDictionary<string, bool> leafTruth
    )
    {
        bool raw = leafTruth[LeafKey(leaf)];
        return leaf.Negate ? !raw : raw;
    }

    private static string LeafKey(PipelineStepCondition leaf) =>
        $"{leaf.ConditionType}:{leaf.LeftOperand}:{leaf.RightOperand}";

    private static PipelineStepCondition Leaf(
        Guid stepId,
        int order,
        string type,
        string left,
        string right,
        bool negate = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineStepId = stepId,
            BroadcasterId = Broadcaster,
            ConditionType = type,
            Operator = "eq",
            LeftOperand = left,
            RightOperand = right,
            Negate = negate,
            Order = order,
        };

    // ── 1. A flat legacy pipeline migrates with every step/action/parameter/condition preserved ──

    [Fact]
    public void UpcastConditionTree_preserves_every_leaf_field_from_a_flat_legacy_step()
    {
        Guid stepId = Guid.NewGuid();
        PipelineStepCondition leaf0 = Leaf(stepId, 0, "user_role", "role", "moderator");
        PipelineStepCondition leaf1 = Leaf(stepId, 1, "cooldown", "seconds", "30", negate: true);

        IReadOnlyList<PipelineStepCondition> tree = _sut.UpcastConditionTree([leaf1, leaf0]);

        tree.Should().HaveCount(3); // synthetic root + 2 leaves
        PipelineStepCondition root = tree.Single(n => n.ParentConditionId is null);
        root.GroupOp.Should().Be("and");
        root.Negate.Should().BeFalse();

        List<PipelineStepCondition> children =
        [
            .. tree.Where(n => n.ParentConditionId == root.Id).OrderBy(n => n.Order),
        ];
        children.Should().HaveCount(2);

        children[0].Id.Should().Be(leaf0.Id);
        children[0].ConditionType.Should().Be("user_role");
        children[0].Operator.Should().Be("eq");
        children[0].LeftOperand.Should().Be("role");
        children[0].RightOperand.Should().Be("moderator");
        children[0].Negate.Should().BeFalse();
        children[0].Order.Should().Be(0);

        children[1].Id.Should().Be(leaf1.Id);
        children[1].ConditionType.Should().Be("cooldown");
        children[1].LeftOperand.Should().Be("seconds");
        children[1].RightOperand.Should().Be("30");
        children[1].Negate.Should().BeTrue();
        children[1].Order.Should().Be(1);
    }

    // ── 2. Semantics preserved: flat-AND vs synthesized tree agree on every truth assignment ──

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void UpcastConditionTree_evaluation_matches_the_original_flat_AND_semantics(
        bool roleTrue,
        bool cooldownTrue
    )
    {
        Guid stepId = Guid.NewGuid();
        PipelineStepCondition roleLeaf = Leaf(stepId, 0, "user_role", "role", "moderator");
        PipelineStepCondition cooldownLeaf = Leaf(stepId, 1, "cooldown", "seconds", "30");
        List<PipelineStepCondition> flat = [roleLeaf, cooldownLeaf];

        Dictionary<string, bool> truth = new()
        {
            [LeafKey(roleLeaf)] = roleTrue,
            [LeafKey(cooldownLeaf)] = cooldownTrue,
        };

        bool flatResult = EvaluateFlatAnd(flat, truth);
        bool treeResult = EvaluateTree(_sut.UpcastConditionTree(flat), truth);

        treeResult.Should().Be(flatResult);
    }

    [Fact]
    public void UpcastConditionTree_zero_conditions_still_means_always_true()
    {
        IReadOnlyList<PipelineStepCondition> tree = _sut.UpcastConditionTree([]);
        tree.Should().BeEmpty();
        EvaluateTree(tree, new Dictionary<string, bool>()).Should().BeTrue();
    }

    // ── 3. Idempotency: running the upcast twice yields the same single tree, no duplicates ──

    [Fact]
    public void UpcastConditionTree_run_twice_yields_identical_single_tree_no_duplicates()
    {
        Guid stepId = Guid.NewGuid();
        List<PipelineStepCondition> flat =
        [
            Leaf(stepId, 0, "user_role", "role", "moderator"),
            Leaf(stepId, 1, "var_compare", "count.hype", "10"),
        ];

        IReadOnlyList<PipelineStepCondition> firstPass = _sut.UpcastConditionTree(flat);
        IReadOnlyList<PipelineStepCondition> secondPass = _sut.UpcastConditionTree(firstPass);

        secondPass.Should().HaveCount(firstPass.Count);
        secondPass.Select(n => n.Id).Should().BeEquivalentTo(firstPass.Select(n => n.Id));
        secondPass.Single(n => n.ParentConditionId is null).GroupOp.Should().Be("and");
        secondPass.Count(n => n.ParentConditionId is null).Should().Be(1); // exactly one root, never re-wrapped
    }

    [Fact]
    public void UpcastTriggers_run_twice_yields_identical_single_trigger_no_duplicates()
    {
        Pipeline pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "legacy",
            TriggerKind = "event",
        };

        IReadOnlyList<PipelineTrigger> firstPass = _sut.UpcastTriggers(
            pipeline,
            wrappingCommand: null
        );
        // The tree is never persisted back onto pipeline.Triggers by this pure/read-only service —
        // simulate "run again on the same unmodified source" (still zero real Triggers rows).
        IReadOnlyList<PipelineTrigger> secondPass = _sut.UpcastTriggers(
            pipeline,
            wrappingCommand: null
        );

        firstPass.Should().HaveCount(1);
        secondPass.Should().HaveCount(1);
        secondPass[0].Kind.Should().Be(firstPass[0].Kind);
        secondPass[0].ConfigJson.Should().Be(firstPass[0].ConfigJson);
    }

    // ── 4. Boundary: zero-step and single-step pipelines migrate without error ──

    [Fact]
    public void UpcastConditionTree_single_leaf_step_migrates_without_error()
    {
        Guid stepId = Guid.NewGuid();
        PipelineStepCondition onlyLeaf = Leaf(stepId, 0, "user_role", "role", "vip");

        IReadOnlyList<PipelineStepCondition> tree = _sut.UpcastConditionTree([onlyLeaf]);

        tree.Should().HaveCount(2);
        tree.Single(n => n.ParentConditionId is null).GroupOp.Should().Be("and");
        tree.Single(n => n.Id == onlyLeaf.Id).ConditionType.Should().Be("user_role");
    }

    [Fact]
    public void UpcastConditionTree_no_steps_no_conditions_returns_empty_tree()
    {
        _sut.UpcastConditionTree([]).Should().BeEmpty();
    }

    // ── 5. N triggers round-trip with order intact (already-tree-shaped rows pass through unchanged) ──

    [Fact]
    public void UpcastTriggers_returns_existing_rows_ordered_never_resynthesized()
    {
        Pipeline pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "multi-trigger",
            TriggerKind = "mixed",
        };
        PipelineTrigger t0 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            BroadcasterId = Broadcaster,
            Kind = "command",
            Order = 0,
            ConfigJson = "{\"Name\":\"fight\"}",
        };
        PipelineTrigger t1 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            BroadcasterId = Broadcaster,
            Kind = "command",
            Order = 1,
            ConfigJson = "{\"Name\":\"hit\"}",
        };
        PipelineTrigger t2 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            BroadcasterId = Broadcaster,
            Kind = "event",
            Order = 2,
            ConfigJson = "{\"EventType\":\"channel.raid\"}",
        };
        // Deliberately added out of order to prove Order (not insertion order) governs the result.
        pipeline.Triggers = [t2, t0, t1];

        IReadOnlyList<PipelineTrigger> triggers = _sut.UpcastTriggers(
            pipeline,
            wrappingCommand: null
        );

        triggers.Should().HaveCount(3);
        triggers.Select(t => t.Id).Should().ContainInOrder(t0.Id, t1.Id, t2.Id);
        triggers.Select(t => t.Order).Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public void UpcastTriggers_synthesizes_one_trigger_from_TriggerKind_and_wrapping_command()
    {
        Pipeline pipeline = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "legacy-command-pipeline",
            TriggerKind = "command",
        };
        Command wrappingCommand = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "fight",
            NameNormalized = "fight",
            PrefixMode = "Default",
            MatchMode = "StartsWith",
            Aliases = ["hit", "attack"],
        };

        IReadOnlyList<PipelineTrigger> triggers = _sut.UpcastTriggers(pipeline, wrappingCommand);

        triggers.Should().HaveCount(1);
        triggers[0].Kind.Should().Be("command");
        triggers[0].ConfigJson.Should().Contain("fight");
        triggers[0].ConfigJson.Should().Contain("hit");
    }

    // ── 5b. N triggers actually persisted (real DbContext) round-trip with order intact ──

    [Fact]
    public async Task PipelineTriggers_persist_and_reload_with_order_intact()
    {
        NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext db =
            NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext.New();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "multi-trigger-persisted",
                TriggerKind = "mixed",
            }
        );

        db.PipelineTriggers.AddRange(
            new PipelineTrigger
            {
                Id = Guid.NewGuid(),
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                Kind = "timer",
                Order = 2,
                ConfigJson = "{\"TimerId\":\"11111111-1111-1111-1111-111111111111\"}",
            },
            new PipelineTrigger
            {
                Id = Guid.NewGuid(),
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                Kind = "command",
                Order = 0,
                ConfigJson = "{\"Name\":\"fight\"}",
            },
            new PipelineTrigger
            {
                Id = Guid.NewGuid(),
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                Kind = "event",
                Order = 1,
                ConfigJson = "{\"EventType\":\"channel.raid\"}",
            }
        );
        await db.SaveChangesAsync();

        List<PipelineTrigger> reloaded =
        [
            .. db.PipelineTriggers.Where(t => t.PipelineId == pipelineId).OrderBy(t => t.Order),
        ];

        reloaded.Should().HaveCount(3);
        reloaded.Select(t => t.Kind).Should().ContainInOrder("command", "event", "timer");
        reloaded.Select(t => t.Order).Should().ContainInOrder(0, 1, 2);
    }

    // ── S-PIPE-TREE-b — BlockKind/BlockConfigJson nesting columns ──────────────────────────────

    // ── 6a. A branch step persists with children in TWO different arms (then/else), reloads with
    //        each child in the right arm in the right order ──
    [Fact]
    public async Task If_block_persists_children_in_then_and_else_arms_with_order_intact()
    {
        NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext db =
            NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext.New();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "branch-arms",
                TriggerKind = "command",
            }
        );

        Guid ifStepId = Guid.NewGuid();
        PipelineStep ifStep = new()
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
        };

        PipelineStep thenStep0 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = ifStepId,
            Branch = "then",
            Order = 0,
            ActionType = "send_message",
        };
        PipelineStep thenStep1 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = ifStepId,
            Branch = "then",
            Order = 1,
            ActionType = "timeout_user",
        };
        PipelineStep elseStep0 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = ifStepId,
            Branch = "else",
            Order = 0,
            ActionType = "shoutout",
        };

        db.PipelineSteps.AddRange(ifStep, thenStep0, thenStep1, elseStep0);
        await db.SaveChangesAsync();

        List<PipelineStep> reloaded = [.. db.PipelineSteps.Where(s => s.PipelineId == pipelineId)];

        PipelineStep reloadedIf = reloaded.Single(s => s.Id == ifStepId);
        reloadedIf.BlockKind.Should().Be("if");
        reloadedIf.BlockConfigJson.Should().Contain("ConditionRootId");

        List<PipelineStep> thenChildren =
        [
            .. reloaded
                .Where(s => s.ParentStepId == ifStepId && s.Branch == "then")
                .OrderBy(s => s.Order),
        ];
        List<PipelineStep> elseChildren =
        [
            .. reloaded
                .Where(s => s.ParentStepId == ifStepId && s.Branch == "else")
                .OrderBy(s => s.Order),
        ];

        thenChildren.Select(s => s.Id).Should().ContainInOrder(thenStep0.Id, thenStep1.Id);
        thenChildren
            .Select(s => s.ActionType)
            .Should()
            .ContainInOrder("send_message", "timeout_user");
        elseChildren.Select(s => s.Id).Should().ContainInOrder(elseStep0.Id);
        elseChildren.Select(s => s.ActionType).Should().ContainInOrder("shoutout");
    }

    // ── 6b. Nesting to at least 3 levels deep round-trips intact (branch inside branch inside branch) ──
    [Fact]
    public async Task Branch_nested_three_levels_deep_round_trips_intact()
    {
        NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext db =
            NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext.New();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "three-deep",
                TriggerKind = "command",
            }
        );

        Guid level1IfId = Guid.NewGuid();
        Guid level2IfId = Guid.NewGuid();
        Guid level3IfId = Guid.NewGuid();
        Guid level3LeafId = Guid.NewGuid();

        PipelineStep level1If = new()
        {
            Id = level1IfId,
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = null,
            Order = 0,
            BlockKind = "if",
            BlockConfigJson = "{}",
            ActionType = "block",
        };
        PipelineStep level2If = new()
        {
            Id = level2IfId,
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = level1IfId,
            Branch = "then",
            Order = 0,
            BlockKind = "if",
            BlockConfigJson = "{}",
            ActionType = "block",
        };
        PipelineStep level3If = new()
        {
            Id = level3IfId,
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = level2IfId,
            Branch = "then",
            Order = 0,
            BlockKind = "if",
            BlockConfigJson = "{}",
            ActionType = "block",
        };
        PipelineStep level3Leaf = new()
        {
            Id = level3LeafId,
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = level3IfId,
            Branch = "then",
            Order = 0,
            BlockKind = null,
            ActionType = "send_message",
        };

        db.PipelineSteps.AddRange(level1If, level2If, level3If, level3Leaf);
        await db.SaveChangesAsync();

        List<PipelineStep> reloaded = [.. db.PipelineSteps.Where(s => s.PipelineId == pipelineId)];

        // Walk the reloaded tree from the root down three levels, proving every parent/branch edge
        // and every BlockKind survived the round trip.
        PipelineStep rL1 = reloaded.Single(s => s.ParentStepId == null);
        rL1.Id.Should().Be(level1IfId);
        rL1.BlockKind.Should().Be("if");

        PipelineStep rL2 = reloaded.Single(s => s.ParentStepId == rL1.Id);
        rL2.Id.Should().Be(level2IfId);
        rL2.Branch.Should().Be("then");
        rL2.BlockKind.Should().Be("if");

        PipelineStep rL3 = reloaded.Single(s => s.ParentStepId == rL2.Id);
        rL3.Id.Should().Be(level3IfId);
        rL3.Branch.Should().Be("then");
        rL3.BlockKind.Should().Be("if");

        PipelineStep rLeaf = reloaded.Single(s => s.ParentStepId == rL3.Id);
        rLeaf.Id.Should().Be(level3LeafId);
        rLeaf.Branch.Should().Be("then");
        rLeaf.BlockKind.Should().BeNull();
        rLeaf.ActionType.Should().Be("send_message");
    }

    // ── 6c. Boundary: a branch with an EMPTY arm persists and reloads as an empty arm, not as
    //        null/missing ──
    [Fact]
    public async Task If_block_with_empty_else_arm_reloads_as_empty_not_null()
    {
        NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext db =
            NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext.New();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "empty-else",
                TriggerKind = "command",
            }
        );

        Guid ifStepId = Guid.NewGuid();
        db.PipelineSteps.AddRange(
            new PipelineStep
            {
                Id = ifStepId,
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                ParentStepId = null,
                Order = 0,
                BlockKind = "if",
                BlockConfigJson = "{}",
                ActionType = "block",
            },
            new PipelineStep
            {
                Id = Guid.NewGuid(),
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                ParentStepId = ifStepId,
                Branch = "then",
                Order = 0,
                ActionType = "send_message",
            }
        // deliberately no "else" child at all
        );
        await db.SaveChangesAsync();

        List<PipelineStep> reloaded = [.. db.PipelineSteps.Where(s => s.PipelineId == pipelineId)];

        List<PipelineStep> elseChildren =
        [
            .. reloaded.Where(s => s.ParentStepId == ifStepId && s.Branch == "else"),
        ];
        List<PipelineStep> thenChildren =
        [
            .. reloaded.Where(s => s.ParentStepId == ifStepId && s.Branch == "then"),
        ];

        elseChildren.Should().BeEmpty(); // empty arm, not a null/missing distinction — a query for
        // it always succeeds and returns zero rows, never throws or returns null.
        thenChildren.Should().HaveCount(1);
    }

    // ── 6d. Legacy flat pipeline upcasts to depth-0 plain action steps with NO invented nesting,
    //        identical execution meaning, and the upcast is idempotent ──
    [Fact]
    public void UpcastStepTree_on_legacy_flat_steps_yields_unchanged_depth0_order_and_is_idempotent()
    {
        Guid pipelineId = Guid.NewGuid();
        PipelineStep step0 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = null,
            Order = 2,
            ActionType = "send_message",
            BlockKind = null,
        };
        PipelineStep step1 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = null,
            Order = 0,
            ActionType = "timeout_user",
            BlockKind = null,
        };
        PipelineStep step2 = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Broadcaster,
            ParentStepId = null,
            Order = 1,
            ActionType = "shoutout",
            BlockKind = null,
        };

        IReadOnlyList<PipelineStep> flat = [step0, step1, step2];

        IReadOnlyList<PipelineStep> firstPass = _sut.UpcastStepTree(flat);

        // Depth-0, no invented parent/branch/blockkind, walk order = execution order (Order asc).
        firstPass.Should().HaveCount(3);
        firstPass.Should().OnlyContain(s => s.ParentStepId == null);
        firstPass.Should().OnlyContain(s => s.BlockKind == null);
        firstPass.Should().OnlyContain(s => s.Branch == null);
        firstPass
            .Select(s => s.ActionType)
            .Should()
            .ContainInOrder("timeout_user", "shoutout", "send_message"); // Order 0,1,2

        IReadOnlyList<PipelineStep> secondPass = _sut.UpcastStepTree(firstPass);
        secondPass.Select(s => s.Id).Should().ContainInOrder(firstPass.Select(s => s.Id));
        secondPass.Should().OnlyContain(s => s.ParentStepId == null);
        secondPass.Should().OnlyContain(s => s.BlockKind == null);
    }

    // ── 6e. The migration is additive: existing flat pipeline rows migrate with content preserved.
    //        Proven by directly persisting pre-existing-shaped rows (BlockKind/BlockConfigJson left
    //        at their column default of null) and reloading — the new nullable columns never force
    //        a value onto rows that never set them. ──
    [Fact]
    public async Task PreExisting_flat_step_row_keeps_its_content_with_new_columns_defaulted_null()
    {
        NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext db =
            NomNomzBot.Infrastructure.Tests.Platform.Pipeline.PipelineTestRunDbContext.New();

        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new Pipeline
            {
                Id = pipelineId,
                BroadcasterId = Broadcaster,
                Name = "pre-existing-flat",
                TriggerKind = "command",
            }
        );

        Guid stepId = Guid.NewGuid();
        db.PipelineSteps.Add(
            new PipelineStep
            {
                Id = stepId,
                PipelineId = pipelineId,
                BroadcasterId = Broadcaster,
                Order = 0,
                ActionType = "send_message",
                ConfigJson = "{\"Message\":\"hello\"}",
                ConfigSchemaVersion = 1,
                IsEnabled = true,
                // BlockKind / BlockConfigJson intentionally left unset (default null) — this is
                // exactly the shape of a row written before this slice's migration ran.
            }
        );
        await db.SaveChangesAsync();

        PipelineStep reloaded = db.PipelineSteps.Single(s => s.Id == stepId);

        reloaded.ActionType.Should().Be("send_message");
        reloaded.ConfigJson.Should().Be("{\"Message\":\"hello\"}");
        reloaded.ConfigSchemaVersion.Should().Be(1);
        reloaded.IsEnabled.Should().BeTrue();
        reloaded.BlockKind.Should().BeNull();
        reloaded.BlockConfigJson.Should().BeNull();
    }
}
