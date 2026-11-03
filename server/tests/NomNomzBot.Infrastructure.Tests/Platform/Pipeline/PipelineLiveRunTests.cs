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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Proves the LIVE pipeline test run: the real actions run with the request's variables as start values, the trigger
/// user defaults to the caller, the run is filed in the run history as <c>manual_live</c> with its step trace, a
/// second start while one runs is refused, and output that cannot be seen is a warning. A request without a mode
/// stays the capture (dry) run.
/// </summary>
public sealed class PipelineLiveRunTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");

    private const string RecordedGraph = """
        {"steps":[{"action":{"type":"record_it","message":"Hi {user}, {viewers} raiders"}}]}
        """;

    private sealed record Call(
        string? Message,
        string User,
        string UserId,
        string TriggeredByUserId,
        string TriggeredByName
    );

    // A real ICommandAction the engine runs like any other; it writes down what the engine handed it. The optional
    // gate lets a test hold the run open to prove the one-live-run-per-pipeline rule.
    private sealed class RecordingAction(string actionType, Func<Task>? gate = null)
        : ICommandAction
    {
        public List<Call> Calls { get; } = [];

        public string ActionType => actionType;
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("message", PipelineActionFieldKind.Text, Templated: true)];

        public async Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Calls.Add(
                new(
                    action.GetString("message"),
                    ctx.Variables.GetValueOrDefault("user", string.Empty),
                    ctx.Variables.GetValueOrDefault("user.id", string.Empty),
                    ctx.TriggeredByUserId,
                    ctx.TriggeredByDisplayName
                )
            );
            if (gate is not null)
                await gate();
            return ActionResult.Success();
        }
    }

    private sealed record Harness(
        PipelineTestRunService Sut,
        PipelineTestRunDbContext Db,
        IOverlayPresenceRegistry Overlays
    );

    private static Harness Build(params ICommandAction[] fixtureActions)
    {
        PipelineTestRunDbContext db = PipelineTestRunDbContext.New();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        user.Username.Returns("caller_name");
        IOverlayPresenceRegistry overlays = Substitute.For<IOverlayPresenceRegistry>();
        ITemplateResolver resolver = TestTemplateResolver.Create();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        IPipelineService pipelines = Substitute.For<IPipelineService>();

        ICommandAction[] actions = [.. fixtureActions, new SetVariableAction()];
        PipelineTestRunService sut = new(
            db,
            tenant,
            actions,
            [new ComparisonCondition(resolver)],
            registry,
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System,
            pipelines,
            user,
            overlays
        );
        return new(sut, db, overlays);
    }

    private static async Task SeedAsync(Harness h, string graphJson = RecordedGraph)
    {
        h.Db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "live",
                IsEnabled = true,
                GraphJsonCache = graphJson,
            }
        );
        await h.Db.SaveChangesAsync();
    }

    private static PipelineTestRunRequest Live(params (string, string)[] vars) =>
        new(vars.ToDictionary(v => v.Item1, v => v.Item2), Mode: PipelineTestRunModes.Live);

    [Fact]
    public async Task A_live_run_calls_the_real_action_with_the_resolved_arguments()
    {
        RecordingAction recorder = new("record_it");
        Harness h = Build(recorder);
        await SeedAsync(h);

        Result<TestRunResultDto> result = await h.Sut.RunAsync(
            PipelineId,
            Live(("user", "viewer_one"), ("user.id", "777"), ("viewers", "42"))
        );

        result.IsSuccess.Should().BeTrue();
        recorder
            .Calls.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new Call("Hi viewer_one, 42 raiders", "viewer_one", "777", "777", "viewer_one"));
        result.Value.Mode.Should().Be("live");
        result.Value.Success.Should().BeTrue();
        result.Value.CapturedEffects.Should().BeEmpty();
        result.Value.RealSteps.Should().Equal("record_it");
    }

    [Fact]
    public async Task A_dry_run_captures_the_same_action_and_never_calls_it()
    {
        RecordingAction recorder = new("record_it");
        Harness h = Build(recorder);
        await SeedAsync(h);

        Result<TestRunResultDto> result = await h.Sut.RunAsync(
            PipelineId,
            new(new Dictionary<string, string> { ["user"] = "viewer_one" })
        );

        recorder.Calls.Should().BeEmpty();
        result.Value.Mode.Should().Be("dry");
        result.Value.CapturedEffects.Should().ContainSingle().Which.Name.Should().Be("record_it");
        result.Value.RealSteps.Should().BeEmpty();
        PipelineExecution row = h.Db.PipelineExecutions.Single();
        row.TriggerKind.Should().Be("test_run");
    }

    [Fact]
    public async Task A_live_run_is_filed_in_the_run_history_as_manual_live_with_its_step_trace()
    {
        RecordingAction recorder = new("record_it");
        Harness h = Build(recorder);
        await SeedAsync(
            h,
            """
            {"steps":[
              {"action":{"type":"set_variable","name":"n","value":"7"}},
              {"action":{"type":"record_it","message":"n is {n}"}}
            ]}
            """
        );

        await h.Sut.RunAsync(PipelineId, Live());

        PipelineExecution row = h.Db.PipelineExecutions.Single();
        row.PipelineId.Should().Be(PipelineId);
        row.BroadcasterId.Should().Be(Channel);
        row.TriggerKind.Should().Be("manual_live");
        row.Status.Should().Be("completed");
        row.TriggeredByUserId.Should().Be(Caller);
        using JsonDocument trace = JsonDocument.Parse(row.StepLogsJson!);
        trace
            .RootElement.EnumerateArray()
            .Select(s =>
                (s.GetProperty("ActionType").GetString(), s.GetProperty("Succeeded").GetBoolean())
            )
            .Should()
            .Equal(("set_variable", true), ("record_it", true));
    }

    [Fact]
    public async Task The_trigger_user_defaults_to_the_caller_so_a_viewer_is_never_hit()
    {
        RecordingAction recorder = new("record_it");
        Harness h = Build(recorder);
        await SeedAsync(h);

        await h.Sut.RunAsync(PipelineId, Live(("viewers", "5")));

        recorder
            .Calls.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Call(
                    "Hi caller_name, 5 raiders",
                    "caller_name",
                    Caller.ToString(),
                    Caller.ToString(),
                    "caller_name"
                )
            );
    }

    [Fact]
    public async Task A_second_live_start_while_one_runs_is_refused_and_the_gate_frees_after_it_ends()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingAction recorder = new(
            "record_it",
            async () =>
            {
                entered.TrySetResult();
                await release.Task;
            }
        );
        Harness h = Build(recorder);
        await SeedAsync(h);

        Task<Result<TestRunResultDto>> first = h.Sut.RunAsync(PipelineId, Live());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Result<TestRunResultDto> second = await h.Sut.RunAsync(PipelineId, Live());

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("LIVE_RUN_IN_PROGRESS");
        recorder.Calls.Should().ContainSingle();

        release.SetResult();
        (await first).IsSuccess.Should().BeTrue();
        Result<TestRunResultDto> third = await h.Sut.RunAsync(PipelineId, Live());
        third.IsSuccess.Should().BeTrue();
        recorder.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_sound_step_with_no_browser_source_attached_is_a_warning_not_a_silent_success()
    {
        RecordingAction sound = new("play_sound");
        Harness h = Build(sound);
        h.Overlays.IsOverlayConnected(Channel).Returns(false);
        await SeedAsync(h, """{"steps":[{"action":{"type":"play_sound","message":"boom"}}]}""");

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Live())).Value;

        result.Success.Should().BeTrue();
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("play_sound")
            .And.Contain("browser source");
    }

    [Fact]
    public async Task A_sound_step_with_a_browser_source_attached_has_no_warning()
    {
        RecordingAction sound = new("play_sound");
        Harness h = Build(sound);
        h.Overlays.IsOverlayConnected(Channel).Returns(true);
        await SeedAsync(h, """{"steps":[{"action":{"type":"play_sound","message":"boom"}}]}""");

        TestRunResultDto result = (await h.Sut.RunAsync(PipelineId, Live())).Value;

        result.Warnings.Should().BeEmpty();
        result.RealSteps.Should().Equal("play_sound");
    }

    [Fact]
    public async Task An_unknown_mode_is_refused_and_runs_nothing()
    {
        RecordingAction recorder = new("record_it");
        Harness h = Build(recorder);
        await SeedAsync(h);

        Result<TestRunResultDto> result = await h.Sut.RunAsync(
            PipelineId,
            new(new Dictionary<string, string>(), Mode: "yolo")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        recorder.Calls.Should().BeEmpty();
        h.Db.PipelineExecutions.Should().BeEmpty();
    }
}
