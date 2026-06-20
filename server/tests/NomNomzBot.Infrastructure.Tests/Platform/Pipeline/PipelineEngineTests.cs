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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

public class InfraPipelineEngineTests
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");

    private static PipelineEngine CreateEngine(IChatProvider? chat = null)
    {
        chat ??= Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IChannelRegistry? registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ICommandAction[] actions = new ICommandAction[]
        {
            new StopAction(),
            new SetVariableAction(),
            new WaitAction(),
        };

        ICommandCondition[] conditions = new ICommandCondition[]
        {
            new UserRoleCondition(),
            new RandomCondition(),
        };

        return new(
            registry,
            actions,
            conditions,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );
    }

    private static PipelineRequest BuildRequest(
        string json,
        Guid? broadcaster = null,
        string user = "user1"
    ) =>
        new()
        {
            BroadcasterId = broadcaster ?? TestChannel,
            TriggeredByUserId = user,
            TriggeredByDisplayName = "TestUser",
            PipelineJson = json,
            MessageId = "msg1",
            RawMessage = "",
        };

    // ─── Basic execution ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptySteps_ReturnsCompleted()
    {
        PipelineEngine engine = CreateEngine();
        PipelineExecutionResult result = await engine.ExecuteAsync(
            BuildRequest("""{"steps":[]}""")
        );

        result.Outcome.Should().Be(PipelineOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsFailed()
    {
        PipelineEngine engine = CreateEngine();
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest("not-json"));

        result.Outcome.Should().Be(PipelineOutcome.Failed);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NullDefinition_ReturnsCompleted()
    {
        // null JSON deserializes to null definition → treated as empty pipeline → Completed
        PipelineEngine engine = CreateEngine();
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest("null"));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ContinuesExecution()
    {
        PipelineEngine engine = CreateEngine();
        string json =
            """{"steps":[{"action":{"type":"does_not_exist"}},{"action":{"type":"stop"}}]}""";
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        // Unknown action is logged as warning and fails the step, but execution continues (fail-open)
        result.StepLogs.Should().HaveCount(2);
    }

    // ─── Stop action ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StopAction_SetsShouldStopAndBreaks()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """
            {
              "steps": [
                {"action":{"type":"stop"}},
                {"action":{"type":"stop"}}
              ]
            }
            """;

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        // Pipeline completes (not failed) but only one step ran
        result.StepsExecuted.Should().Be(1);
    }

    // ─── SetVariable ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SetVariable_StoresInContext()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """
            {
              "steps": [
                {"action":{"type":"set_variable","name":"myvar","value":"hello"}},
                {"action":{"type":"stop"}}
              ]
            }
            """;

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result.StepLogs.Should().HaveCount(2);
    }

    // ─── Conditions ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConditionFalse_SkipsStep()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """
            {
              "steps": [
                {
                  "condition": {"type":"user_role","min_role":"moderator"},
                  "action": {"type":"stop"}
                }
              ]
            }
            """;
        // No user.role variable → defaults to viewer → condition false → skip
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.StepsSkipped.Should().Be(1);
        result.StepsExecuted.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ConditionTrue_ExecutesStep()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """
            {
              "steps": [
                {
                  "condition": {"type":"user_role","min_role":"moderator"},
                  "action": {"type":"stop"}
                }
              ]
            }
            """;
        PipelineRequest request = new()
        {
            BroadcasterId = TestChannel,
            TriggeredByUserId = "mod1",
            TriggeredByDisplayName = "Mod1",
            PipelineJson = json,
            MessageId = "m1",
            RawMessage = "",
            InitialVariables = { { "user.role", "moderator" } },
        };

        PipelineExecutionResult result = await engine.ExecuteAsync(request);

        result.StepsExecuted.Should().Be(1);
    }

    // ─── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelled_ReturnsCancelled()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """{"steps":[{"action":{"type":"wait","milliseconds":5000}}]}""";

        using CancellationTokenSource cts = new();
        cts.Cancel();

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json), cts.Token);

        result.Outcome.Should().Be(PipelineOutcome.Cancelled);
    }

    // ─── Concurrency tracking ─────────────────────────────────────────────────

    [Fact]
    public void GetActiveCountForChannel_NoActivePipelines_ReturnsZero()
    {
        PipelineEngine engine = CreateEngine();
        engine.GetActiveCountForChannel(TestChannel).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AfterCompletion_DecrementsActiveCount()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """{"steps":[{"action":{"type":"stop"}}]}""";

        await engine.ExecuteAsync(BuildRequest(json));

        engine.GetActiveCountForChannel(TestChannel).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsConcurrencyLimit_ReturnsFailed()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """{"steps":[{"action":{"type":"wait","milliseconds":5000}}]}""";

        // Start 5 long-running pipelines
        List<CancellationTokenSource> ctsList = Enumerable
            .Range(0, 5)
            .Select(_ => new CancellationTokenSource())
            .ToList();
        Task<PipelineExecutionResult>[] longTasks = ctsList
            .Select(cts => engine.ExecuteAsync(BuildRequest(json, TestChannel), cts.Token))
            .ToArray();

        await Task.Delay(100); // Let them register

        // 6th should fail
        PipelineExecutionResult overflow = await engine.ExecuteAsync(
            BuildRequest(json, TestChannel)
        );

        overflow.Outcome.Should().Be(PipelineOutcome.Failed);
        overflow.ErrorMessage.Should().NotBeNullOrEmpty();

        ctsList.ForEach(cts => cts.Cancel());
        try
        {
            await Task.WhenAll(longTasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        { /* expected cancellations */
        }
        ctsList.ForEach(cts => cts.Dispose());
    }

    // ─── StopOnMatch ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StopOnMatch_StopsAfterSuccessfulStep()
    {
        PipelineEngine engine = CreateEngine();
        const string json = """
            {
              "steps": [
                {"action":{"type":"set_variable","name":"x","value":"1"},"stop_on_match":true},
                {"action":{"type":"set_variable","name":"y","value":"2"}}
              ]
            }
            """;

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        // stop_on_match=true on step 0, so only 1 step executed
        result.StepsExecuted.Should().Be(1);
    }
}
