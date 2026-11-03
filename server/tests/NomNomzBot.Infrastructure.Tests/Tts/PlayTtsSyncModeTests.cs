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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the <c>mode</c> field of <c>play_tts</c>: <c>async</c> returns at dispatch, <c>sync</c> returns only
/// after the clip has had its measured length (plus a start allowance) to play, so a later step such as a music
/// volume restore runs after the speech ends. The clock is a fake; the real action runs.
/// </summary>
public sealed class PlayTtsSyncModeTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-1111-7000-8000-000000000002");
    private static readonly TimeSpan StartAllowance = TimeSpan.FromMilliseconds(250);

    private static PipelineExecutionContext Context(CancellationToken token = default) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!tts hi",
            CancellationToken = token,
        };

    private static ActionDefinition Action(string? mode)
    {
        Dictionary<string, JsonElement> parameters = new()
        {
            ["text"] = JsonSerializer.SerializeToElement("hello"),
        };
        if (mode is not null)
            parameters["mode"] = JsonSerializer.SerializeToElement(mode);
        return new() { Type = "play_tts", Parameters = parameters };
    }

    private static (PlayTtsAction Action, FakeTimeProvider Clock) Build(
        Result<TtsDispatchOutcome> outcome
    )
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult(ci.ArgAt<string>(0)));
        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(outcome);
        FakeTimeProvider clock = new();
        return (new(resolver, dispatch, clock), clock);
    }

    private static Result<TtsDispatchOutcome> Dispatched(int durationMs) =>
        Result.Success(
            new TtsDispatchOutcome(
                TtsDispatchDisposition.Dispatched,
                "voice-7",
                "edge",
                5,
                durationMs,
                "https://bot.local/sounds/tts.mp3"
            )
        );

    [Fact]
    public async Task Sync_DoesNotCompleteUntilTheClipLengthPlusAllowanceHasPassed()
    {
        (PlayTtsAction action, FakeTimeProvider clock) = Build(Dispatched(4000));
        PipelineExecutionContext ctx = Context();

        Task<ActionResult> run = action.ExecuteAsync(ctx, Action("sync"));

        run.IsCompleted.Should().BeFalse("the clip is still playing");
        clock.Advance(
            TimeSpan.FromMilliseconds(4000) + StartAllowance - TimeSpan.FromMilliseconds(1)
        );
        run.IsCompleted.Should().BeFalse("one millisecond of the clip is left");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        ActionResult result = await run;

        result.Succeeded.Should().BeTrue();
        ctx.Variables["tts.durationMs"].Should().Be("4000");
        ctx.Variables["tts.voiceId"].Should().Be("voice-7");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("async")]
    public async Task Async_ReturnsAtOnce_AndStillSetsTheVariables(string? mode)
    {
        (PlayTtsAction action, FakeTimeProvider clock) = Build(Dispatched(4000));
        PipelineExecutionContext ctx = Context();

        ActionResult result = await action.ExecuteAsync(ctx, Action(mode));

        result.Succeeded.Should().BeTrue();
        clock.GetTimestamp().Should().Be(new FakeTimeProvider().GetTimestamp());
        ctx.Variables["tts.durationMs"].Should().Be("4000");
        ctx.Variables["tts.voiceId"].Should().Be("voice-7");
    }

    [Fact]
    public async Task Sync_AClipLongerThanTheCapWaitsTheCapPlusAllowance()
    {
        (PlayTtsAction action, FakeTimeProvider clock) = Build(Dispatched(300_000));
        PipelineExecutionContext ctx = Context();

        Task<ActionResult> run = action.ExecuteAsync(ctx, Action("sync"));

        clock.Advance(
            TimeSpan.FromMilliseconds(60_000) + StartAllowance - TimeSpan.FromMilliseconds(1)
        );
        run.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        ActionResult result = await run;

        result.Succeeded.Should().BeTrue();
        ctx.Variables["tts.durationMs"].Should().Be("300000", "the variable keeps the real length");
    }

    [Fact]
    public async Task Sync_ADispatchFailureReturnsAtOnceWithTheReason_AndSetsNoVariables()
    {
        (PlayTtsAction action, FakeTimeProvider _) = Build(
            Result.Failure<TtsDispatchOutcome>("TTS is off for this channel.", "TTS_DISABLED")
        );
        PipelineExecutionContext ctx = Context();

        ActionResult result = await action.ExecuteAsync(ctx, Action("sync"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("TTS is off for this channel.");
        ctx.Variables.Should().NotContainKey("tts.durationMs");
    }

    [Fact]
    public async Task Sync_AQueuedLineWaitsNothing_BecauseNothingPlaysYet()
    {
        Result<TtsDispatchOutcome> queued = Result.Success(
            new TtsDispatchOutcome(TtsDispatchDisposition.Queued, "voice-7", "edge", 5, 0, null)
        );
        (PlayTtsAction action, FakeTimeProvider _) = Build(queued);

        ActionResult result = await action.ExecuteAsync(Context(), Action("sync"));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_CancellationDuringTheWaitStopsIt()
    {
        (PlayTtsAction action, FakeTimeProvider clock) = Build(Dispatched(4000));
        using CancellationTokenSource cts = new();

        Task<ActionResult> run = action.ExecuteAsync(Context(cts.Token), Action("sync"));
        clock.Advance(TimeSpan.FromMilliseconds(1000));
        run.IsCompleted.Should().BeFalse();
        await cts.CancelAsync();

        Func<Task> act = async () => await run;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UnknownMode_FailsWithAClearMessage_AndDispatchesNothing()
    {
        (PlayTtsAction action, FakeTimeProvider _) = Build(Dispatched(4000));

        ActionResult result = await action.ExecuteAsync(Context(), Action("later"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("mode").And.Contain("async").And.Contain("sync");
    }

    [Fact]
    public void ModeField_IsAnEnumOfAsyncAndSync()
    {
        (PlayTtsAction action, FakeTimeProvider _) = Build(Dispatched(1));

        PipelineActionFieldDescriptor field = action.Fields.Single(f => f.Name == "mode");

        field.Kind.Should().Be(PipelineActionFieldKind.Enum);
        field.Options.Should().Equal("async", "sync");
        field.Description!.Key.Should().Be("pipeline.play_tts.mode.help");
    }
}
