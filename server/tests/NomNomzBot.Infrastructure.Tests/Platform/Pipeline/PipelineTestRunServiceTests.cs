// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NomNomzBot.Infrastructure.Moderation.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Proves the pipeline DRY-RUN (commands-pipelines.md): side-effecting actions are CAPTURED (chat + TTS seams never
/// fire), a captured chat message's template resolves so its text surfaces, conditions are evaluated for real against
/// live variables, and a captured action's success lets a downstream step run.
/// </summary>
public sealed class PipelineTestRunServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");

    private sealed record Harness(
        PipelineTestRunService Sut,
        IChatProvider Chat,
        ITtsDispatchService Tts
    );

    private static PipelineTestRunDbContext NewDb() => PipelineTestRunDbContext.New();

    private static Harness Build(PipelineTestRunDbContext db)
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);

        IChatProvider chat = Substitute.For<IChatProvider>();
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();

        // A resolver that performs simple {var}/{{var}} interpolation from the pipeline variables, so captured chat
        // text and real conditions see the same values production would — but nothing external is involved.
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                Task.FromResult(
                    Interpolate(ci.ArgAt<string>(0), ci.ArgAt<IDictionary<string, string>>(1))
                )
            );

        ICommandAction[] actions =
        [
            new SendMessageAction(chat, resolver),
            new PlayTtsAction(resolver, tts),
            new SetVariableAction(),
            new BanAction(chat),
        ];
        ICommandCondition[] conditions = [new ComparisonCondition(resolver)];

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        return new(
            new(
                db,
                tenant,
                actions,
                conditions,
                registry,
                resolver,
                NullLogger<PipelineEngine>.Instance,
                TimeProvider.System
            ),
            chat,
            tts
        );
    }

    private static string Interpolate(string template, IDictionary<string, string> vars) =>
        Regex.Replace(
            template,
            @"\{\{?([a-zA-Z0-9_.]+)\}?\}",
            m => vars.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value
        );

    private static async Task SeedPipelineAsync(
        PipelineTestRunDbContext db,
        params PipelineStep[] steps
    )
    {
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "p",
                IsEnabled = true,
            }
        );
        foreach (PipelineStep step in steps)
            db.PipelineSteps.Add(step);
        await db.SaveChangesAsync();
    }

    private static PipelineStep Step(
        int order,
        string actionType,
        string configJson,
        params PipelineStepCondition[] conditions
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineId = PipelineId,
            BroadcasterId = Channel,
            Order = order,
            ActionType = actionType,
            ConfigJson = configJson,
            IsEnabled = true,
            Conditions = conditions,
        };

    private static PipelineStepCondition Comparison(string left, string op, string right) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineStepId = Guid.Empty,
            BroadcasterId = Channel,
            ConditionType = "comparison",
            Operator = op,
            LeftOperand = left,
            RightOperand = right,
            Order = 0,
        };

    private static PipelineTestRunRequest Request(params (string, string)[] vars) =>
        new(vars.ToDictionary(v => v.Item1, v => v.Item2));

    [Fact]
    public async Task Runs_a_graph_only_pipeline_the_production_shape_with_no_step_rows()
    {
        // Real pipelines (imported bundles, the dashboard builder) persist ONLY GraphJsonCache — no
        // PipelineStep rows. The test-run must load that graph like the real dispatch does; passing only the
        // id ran zero steps (the bug this locks down).
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "graph-only",
                IsEnabled = true,
                GraphJsonCache = """
                {"steps":[{"action":{"type":"send_message","message":"from the graph {who}"}}]}
                """,
            }
        );
        await db.SaveChangesAsync();

        TestRunResultDto result = (
            await h.Sut.RunAsync(PipelineId, Request(("who", "cache")))
        ).Value;

        result.Success.Should().BeTrue();
        result
            .CapturedEffects.Select(e => e.Name)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("send_message");
        result.ChatOutput.Should().ContainSingle().Which.Should().Be("from the graph cache");
    }

    [Fact]
    public async Task Captures_chat_and_tts_without_firing_either_seam()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        await SeedPipelineAsync(
            db,
            Step(0, "send_message", """{"type":"send_message","message":"hi {who}"}"""),
            Step(1, "play_tts", """{"type":"play_tts","text":"speak up"}""")
        );

        TestRunResultDto result = (
            await h.Sut.RunAsync(PipelineId, Request(("who", "chat")))
        ).Value;

        result.Success.Should().BeTrue();
        // Neither outward seam was touched.
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await h.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!, default);
        // Both side-effecting actions were captured; the chat template resolved to its real text.
        result
            .CapturedEffects.Select(e => e.Name)
            .Should()
            .BeEquivalentTo("send_message", "play_tts");
        result.ChatOutput.Should().ContainSingle().Which.Should().Be("hi chat");
    }

    [Fact]
    public async Task A_captured_actions_success_lets_a_real_condition_gate_a_downstream_step()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        await SeedPipelineAsync(
            db,
            Step(0, "play_tts", """{"type":"play_tts","text":"go"}"""),
            // Runs only if the just-captured action reported success — {last.success} is set by the engine and the
            // comparison is evaluated for real.
            Step(
                1,
                "set_variable",
                """{"type":"set_variable","name":"ran","value":"yes"}""",
                Comparison("{last.success}", "eq", "true")
            )
        );

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Success.Should().BeTrue();
        // The set_variable (passthrough, real) executed because the captured play_tts reported success.
        result.Log.Should().Contain(l => l.Contains("set_variable") && l.Contains("ran=yes"));
    }

    [Fact]
    public async Task A_false_condition_is_evaluated_for_real_and_skips_its_step()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        await SeedPipelineAsync(
            db,
            Step(
                0,
                "set_variable",
                """{"type":"set_variable","name":"ran","value":"yes"}""",
                Comparison("1", "gt", "5")
            )
        );

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Success.Should().BeTrue();
        result.Log.Should().Contain(l => l.Contains("set_variable") && l.Contains("skipped"));
    }

    [Fact]
    public async Task Captures_a_destructive_moderation_action_without_ever_calling_the_chat_provider()
    {
        // Destructive actions (ban/timeout/role-remove) must be captured, never executed — the real ban
        // seam (IChatProvider.BanUserAsync) must never fire during a dry-run.
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        await SeedPipelineAsync(
            db,
            Step(0, "ban", """{"type":"ban","user_id":"12345","reason":"test"}""")
        );

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Success.Should().BeTrue();
        await h.Chat.DidNotReceiveWithAnyArgs().BanUserAsync(default, default!, default, default);
        result
            .CapturedEffects.Select(e => e.Name)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("ban");
    }

    [Fact]
    public async Task A_test_run_traces_every_executed_step_in_order_with_the_branch_and_variable_changes()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        PipelineStep setScore = Step(
            0,
            "set_variable",
            """{"type":"set_variable","name":"score","value":"5"}"""
        );
        PipelineStep ifBlock = Step(1, "if", "{}", Comparison("{score}", "gt", "3"));
        ifBlock.BlockKind = "if";
        PipelineStep thenSay = Step(
            0,
            "send_message",
            """{"type":"send_message","message":"big {score}"}"""
        );
        thenSay.ParentStepId = ifBlock.Id;
        thenSay.Branch = "then";
        PipelineStep elseSay = Step(
            0,
            "send_message",
            """{"type":"send_message","message":"small"}"""
        );
        elseSay.ParentStepId = ifBlock.Id;
        elseSay.Branch = "else";
        await SeedPipelineAsync(db, setScore, ifBlock, thenSay, elseSay);

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Success.Should().BeTrue();
        result.Trace.Select(t => t.StepType).Should().Equal("set_variable", "if", "send_message");
        result
            .Trace.Select(t => t.StepId)
            .Should()
            .Equal(setScore.Id.ToString(), ifBlock.Id.ToString(), thenSay.Id.ToString());

        PipelineTraceStepDto set = result.Trace[0];
        set.VariableChanges.Should().ContainSingle();
        set.VariableChanges[0].Key.Should().Be("score");
        set.VariableChanges[0].Before.Should().BeNull();
        set.VariableChanges[0].After.Should().Be("5");

        result.Trace[1].Branch.Should().Be("then");
        result.Trace[1].VariableChanges.Should().BeEmpty();

        result.Trace[2].Output.Should().Be("big 5");
        result.Trace[2].Error.Should().BeNull();
    }

    [Fact]
    public async Task A_traced_loop_row_counts_its_iterations()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        PipelineStep loop = Step(0, "loop", "{}");
        loop.BlockKind = "loop";
        loop.BlockConfigJson = """{"mode":"repeat","count":3}""";
        PipelineStep body = Step(
            0,
            "send_message",
            """{"type":"send_message","message":"tick {loop.index}"}"""
        );
        body.ParentStepId = loop.Id;
        await SeedPipelineAsync(db, loop, body);

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Trace.Should().HaveCount(4);
        result.Trace[0].StepType.Should().Be("loop");
        result.Trace[0].Iterations.Should().Be(3);
        result.Trace.Skip(1).Select(t => t.Output).Should().Equal("tick 0", "tick 1", "tick 2");
    }

    [Fact]
    public async Task A_failed_step_row_carries_its_error()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);
        await SeedPipelineAsync(db, Step(0, "no_such_action", """{"type":"no_such_action"}"""));

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Request())).Value;

        result.Success.Should().BeFalse();
        result.Trace.Should().ContainSingle();
        result.Trace[0].StepType.Should().Be("no_such_action");
        result.Trace[0].Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_unknown_pipeline_is_not_found()
    {
        PipelineTestRunDbContext db = NewDb();
        Harness h = Build(db);

        Result<TestRunResultDto> result = await h.Sut.RunAsync(Guid.NewGuid(), Request());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
