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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
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
            .Returns(true);

        IChannelRegistry? registry = Substitute.For<IChannelRegistry>();
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

        ICommandAction[] actions =
        [
            new StopAction(),
            new SetVariableAction(),
            new WaitAction(resolver),
        ];

        ICommandCondition[] conditions = [new UserRoleCondition(), new RandomCondition()];

        // Tests pass PipelineJson directly — PipelineId is never set, so DB step lookup is never hit.
        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db =
            Substitute.For<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>();

        return new(
            db,
            registry,
            actions,
            conditions,
            resolver,
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

        // Unknown action fails the step; fail-CLOSED means the pipeline aborts after it.
        result.StepLogs.Should().HaveCount(1);
        result.StepLogs[0].Succeeded.Should().BeFalse();
    }

    // ─── Automation auto-provisioned pipelines ───────────────────────────────

    /// <summary>
    /// Proves the exact GraphJsonCache shape AutomationPairingService.EnsureMusicActionPipelinesAsync
    /// writes for each auto-provisioned Stream Deck pipeline — {"steps":[{"action":{"type":"music_..."}}]}
    /// via JsonSerializer.Serialize(PipelineDefinition) — is not just valid JSON but actually reaches and
    /// runs a real ICommandAction through the real engine. This is the layer the DTO-level pairing tests
    /// and the isolated PipelineJson-wiring fix could never see: whether the auto-provisioned row a real
    /// Stream Deck button invokes actually executes anything, as opposed to completing with zero steps
    /// run (the exact silent-no-op failure mode this whole feature exists to close).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AutoProvisionedMusicActionShape_ActuallyInvokesTheAction()
    {
        RecordingAction recorded = new();
        IChatProvider chat = Substitute.For<IChatProvider>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db =
            Substitute.For<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>();

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        PipelineEngine engine = new(
            db,
            registry,
            [recorded],
            [],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );

        string graphJsonCache = System.Text.Json.JsonSerializer.Serialize(
            new PipelineDefinition { Steps = [new() { Action = new() { Type = "music_play" } }] }
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(graphJsonCache));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result
            .StepsExecuted.Should()
            .Be(1, "a zero-step Completed result is the silent no-op bug this closes");
        recorded
            .Invoked.Should()
            .BeTrue("the real music_play action must actually run, not just parse");
    }

    private sealed class RecordingAction : ICommandAction
    {
        public bool Invoked { get; private set; }
        public string ActionType => "music_play";

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Invoked = true;
            return Task.FromResult(ActionResult.Success());
        }
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

        // The second step is a deliberate `stop` — Stopped, not Completed (see the dedicated Stopped test).
        result.Outcome.Should().Be(PipelineOutcome.Stopped);
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
        List<CancellationTokenSource> ctsList =
        [
            .. Enumerable.Range(0, 5).Select(_ => new CancellationTokenSource()),
        ];
        Task<PipelineExecutionResult>[] longTasks =
        [
            .. ctsList.Select(cts =>
                engine.ExecuteAsync(BuildRequest(json, TestChannel), cts.Token)
            ),
        ];

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

    // ─── Outcome threading (S008) ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MiddleStepSendFails_ReturnsPartiallyFailed()
    {
        // A middle step that fails to send must break the run and report PartiallyFailed — never
        // Completed — and the third step must never run.
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), "first", Arg.Any<CancellationToken>()).Returns(true);
        chat.SendMessageAsync(Arg.Any<Guid>(), "second", Arg.Any<CancellationToken>())
            .Returns(false);
        chat.SendMessageAsync(Arg.Any<Guid>(), "third", Arg.Any<CancellationToken>()).Returns(true);

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db =
            Substitute.For<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>();

        PipelineEngine engine = new(
            db,
            registry,
            [new NomNomzBot.Infrastructure.Chat.PipelineActions.SendMessageAction(chat, resolver)],
            [],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );

        const string json = """
            {
              "steps": [
                {"action":{"type":"send_message","message":"first"}},
                {"action":{"type":"send_message","message":"second"}},
                {"action":{"type":"send_message","message":"third"}}
              ]
            }
            """;

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        result.StepsExecuted.Should().Be(1, "only the first step's send actually succeeded");
        result.StepLogs.Should().HaveCount(2, "the run breaks after the failing second step");
        result.StepLogs[1].Succeeded.Should().BeFalse();
        await chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), "third", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StopAction_ReturnsStopped_NotCompleted()
    {
        // A deliberate stop is a distinct outcome from a plain finish — the command still did its work.
        PipelineEngine engine = CreateEngine();
        const string json = """{"steps":[{"action":{"type":"stop"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.Outcome.Should().Be(PipelineOutcome.Stopped);
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
