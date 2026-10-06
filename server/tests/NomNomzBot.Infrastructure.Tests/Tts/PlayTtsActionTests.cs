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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the <c>play_tts</c> pipeline action (tts.md §6): it resolves the <c>text</c> template and hands the
/// utterance to the dispatch service, returning success on dispatch and surfacing the gate's reason on rejection.
/// A missing/empty text fails loudly instead of dispatching junk.
/// </summary>
public sealed class PlayTtsActionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-1111-7000-8000-000000000001");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!tts hi",
            CancellationToken = default,
        };

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "play_tts",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    private static (PlayTtsAction Action, ITtsDispatchService Dispatch) Build(string resolvedText)
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(resolvedText));

        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        return (new(resolver, dispatch), dispatch);
    }

    /// <summary>Resolver that returns a distinct value per input template, so text and voice can differ.</summary>
    private static (PlayTtsAction Action, ITtsDispatchService Dispatch) BuildWithTemplateMap(
        IReadOnlyDictionary<string, string> templateToResolved
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
            .Returns(ci => Task.FromResult(templateToResolved[ci.ArgAt<string>(0)]));

        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        return (new(resolver, dispatch), dispatch);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesText_AndDispatchesIt()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hello resolved");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        14,
                        900,
                        "https://bot.local/sounds/tts.mp3"
                    )
                )
            );

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "{{args}}")));

        result.Succeeded.Should().BeTrue();
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Text == "hello resolved"
                    && r.BroadcasterId == Channel
                    && r.RequestedByTwitchUserId == "viewer-9"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_MissingText_FailsWithoutDispatch()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("unused");

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await dispatch
            .DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DispatchRejects_SurfacesTheReason()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hello");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TtsDispatchOutcome>(
                    "TTS is disabled for this channel.",
                    "FEATURE_DISABLED"
                )
            );

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "{{args}}")));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("TTS is disabled for this channel.");
    }

    // S-TTS-TEMPLATED-VOICE worked example — the owner's own case: a watch-streak event response speaks its
    // message via play_tts, choosing the voice from a template too (e.g. the viewer's own assigned voice stored
    // in a pipeline variable), not a hardcoded id. Proves BOTH the resolved text AND the resolved voice reach the
    // dispatch call — the voice field is template-resolved exactly like text, not passed through raw.
    [Fact]
    public async Task ExecuteAsync_WatchStreakEventResponse_ResolvesTemplatedTextAndTemplatedVoice()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = BuildWithTemplateMap(
            new Dictionary<string, string>
            {
                ["{{user.name}}'s watch streak just hit {{streak.count}} days!"] =
                    "viewer-9's watch streak just hit 30 days!",
                ["{{user.voice}}"] = "en-US-AriaNeural",
            }
        );
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "en-US-AriaNeural",
                        "edge",
                        41,
                        1800,
                        "https://bot.local/sounds/tts.mp3"
                    )
                )
            );

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(
                ("text", "{{user.name}}'s watch streak just hit {{streak.count}} days!"),
                ("voice", "{{user.voice}}")
            )
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Text == "viewer-9's watch streak just hit 30 days!"
                    && r.VoiceIdOverride == "en-US-AriaNeural"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // Unknown-voice honest failure (project truthful-data rule): the dispatch service's reject reason must
    // surface through the action unchanged — never swallowed, never reported as if a different voice spoke.
    [Fact]
    public async Task ExecuteAsync_TemplatedVoiceResolvesToUnknownVoice_SurfacesTheHonestRejection()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = BuildWithTemplateMap(
            new Dictionary<string, string>
            {
                ["{{args}}"] = "hello",
                ["{{user.voice}}"] = "not-a-real-voice",
            }
        );
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TtsDispatchOutcome>(
                    "TTS voice 'not-a-real-voice' does not exist.",
                    "VALIDATION_FAILED"
                )
            );

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("text", "{{args}}"), ("voice", "{{user.voice}}"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("TTS voice 'not-a-real-voice' does not exist.");
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.VoiceIdOverride == "not-a-real-voice"),
                Arg.Any<CancellationToken>()
            );
    }

    // S-REPLAY-TTS-PIPELINE-CORRELATION: a reward redemption's pipeline action chain runs with the
    // redemption's own ChannelEvent id on the execution context (RewardRedeemedHandler → PipelineRequest →
    // PipelineExecutionContext); play_tts must forward it into the dispatch request unchanged so the
    // eventual TtsUtteranceDispatchedEvent — and the RenderedAlertCapture it produces — can correlate back
    // to that redemption for Replay.
    [Fact]
    public async Task ExecuteAsync_ContextCarriesAChannelEventId_ForwardsItToTheDispatchRequest()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hello resolved");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        14,
                        900,
                        "https://bot.local/sounds/tts.mp3"
                    )
                )
            );
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = string.Empty,
            ChannelEventId = "019f2a00-2222-7000-8000-000000000002",
            CancellationToken = default,
        };

        ActionResult result = await action.ExecuteAsync(ctx, Action(("text", "{{args}}")));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.ChannelEventId == "019f2a00-2222-7000-8000-000000000002"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>A standalone chat-command context (no triggering ChannelEvent) forwards null — never invented.</summary>
    [Fact]
    public async Task ExecuteAsync_ContextWithNoChannelEventId_ForwardsNull()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hi");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        2,
                        400,
                        null
                    )
                )
            );

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "{{args}}")));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.ChannelEventId == null),
                Arg.Any<CancellationToken>()
            );
    }

    private static Task<ActionResult> RunAsAsync(
        string asValue,
        string resolvedChannelId,
        out ITtsDispatchService dispatch
    )
    {
        (PlayTtsAction action, ITtsDispatchService d) = BuildWithTemplateMap(
            new Dictionary<string, string>
            {
                ["hello"] = "hello",
                [asValue] = asValue,
                ["{{channel.id}}"] = resolvedChannelId,
            }
        );
        d.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        5,
                        400,
                        null
                    )
                )
            );
        dispatch = d;
        return action.ExecuteAsync(Context(), Action(("text", "hello"), ("as", asValue)));
    }

    [Fact]
    public async Task ExecuteAsync_AsBroadcaster_SpeaksAsTheBroadcastersTwitchId_SoTheirSavedVoiceIsUsed()
    {
        ActionResult result = await RunAsAsync("broadcaster", "chan-77", out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RequestedByTwitchUserId == "chan-77"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_AsBroadcaster_WithNoResolvableChannelId_NamesNobody_SoTheChannelDefaultSpeaks()
    {
        ActionResult result = await RunAsAsync(
            "broadcaster",
            string.Empty,
            out ITtsDispatchService d
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RequestedByTwitchUserId == string.Empty),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("channel")]
    [InlineData("default")]
    public async Task ExecuteAsync_AsBotChannelOrDefault_StillSpeaksWithTheChannelDefault(
        string who
    )
    {
        ActionResult result = await RunAsAsync(who, "chan-77", out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RequestedByTwitchUserId == string.Empty),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("", TtsSpeaker.Bot)]
    [InlineData("bot", TtsSpeaker.Bot)]
    [InlineData("channel", TtsSpeaker.Bot)]
    [InlineData("default", TtsSpeaker.Bot)]
    [InlineData("viewer", TtsSpeaker.Viewer)]
    [InlineData("user", TtsSpeaker.Viewer)]
    [InlineData("trigger", TtsSpeaker.Viewer)]
    [InlineData("broadcaster", TtsSpeaker.Viewer)]
    [InlineData("some-platform-id", TtsSpeaker.Viewer)]
    public async Task ExecuteAsync_TheAsField_PicksWhoseVoiceReadsTheLine_BlankIsTheBot(
        string asValue,
        TtsSpeaker expected
    )
    {
        ActionResult result = await RunAsAsync(asValue, "chan-77", out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.Speaker == expected),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_AsViewer_NamesTheTriggeringUser_SoTheirSavedVoiceReadsTheirWords()
    {
        ActionResult result = await RunAsAsync("viewer", "chan-77", out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Speaker == TtsSpeaker.Viewer && r.RequestedByTwitchUserId == "viewer-9"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static Task<ActionResult> RunWithRateAsync(
        object? rate,
        out ITtsDispatchService dispatch
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
        ITtsDispatchService d = Substitute.For<ITtsDispatchService>();
        d.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        5,
                        400,
                        null
                    )
                )
            );
        dispatch = d;
        PlayTtsAction action = new(resolver, d);
        ActionDefinition definition = rate is null
            ? Action(("text", "hello"))
            : Action(("text", "hello"), ("rate", rate));
        return action.ExecuteAsync(Context(), definition);
    }

    [Theory]
    [InlineData("+30%", 30d)]
    [InlineData("30", 30d)]
    [InlineData("-25%", -25d)]
    [InlineData("-25.5", -25.5d)]
    [InlineData("0%", 0d)]
    public async Task ExecuteAsync_Rate_IsCarriedOntoTheDispatchRequest(
        string rate,
        double expected
    )
    {
        ActionResult result = await RunWithRateAsync(rate, out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RatePercent == expected),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_RateGivenAsJsonNumber_IsCarriedOntoTheDispatchRequest()
    {
        ActionResult result = await RunWithRateAsync(30, out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RatePercent == 30d),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("+300%", 50d)]
    [InlineData("-90", -50d)]
    public async Task ExecuteAsync_RateOutOfRange_IsClampedToPlusMinusFifty(
        string rate,
        double expected
    )
    {
        ActionResult result = await RunWithRateAsync(rate, out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RatePercent == expected),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task ExecuteAsync_RateNotANumber_FailsWithoutDispatch(string rate)
    {
        ActionResult result = await RunWithRateAsync(rate, out ITtsDispatchService d);

        result.Succeeded.Should().BeFalse();
        await d.DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoRate_LeavesTheProviderDefaultRate()
    {
        ActionResult result = await RunWithRateAsync(null, out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RatePercent == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void Fields_ListTheRateField()
    {
        PlayTtsAction action = new(
            Substitute.For<ITemplateResolver>(),
            Substitute.For<ITtsDispatchService>()
        );

        action.Fields.Should().Contain(f => f.Name == "rate" && f.Templated && !f.Required);
    }

    [Theory]
    [InlineData("user", true)]
    [InlineData("broadcaster", false)]
    public async Task ExecuteAsync_OnlyAViewersLine_MayGetAnAssignedVoice(
        string asValue,
        bool expectedAssign
    )
    {
        ActionResult result = await RunAsAsync(asValue, "chan-77", out ITtsDispatchService d);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await d.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.AssignVoiceIfMissing == expectedAssign),
                Arg.Any<CancellationToken>()
            );
    }

    private static Result<TtsDispatchOutcome> Outcome() =>
        Result.Success(
            new TtsDispatchOutcome(TtsDispatchDisposition.Dispatched, "v1", "edge", 20, 900, "u")
        );

    [Fact]
    public async Task ExecuteAsync_Segments_DispatchesOneRequestCarryingAllSegments()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = BuildWithTemplateMap(
            new Dictionary<string, string>
            {
                ["Welcome {{user}}"] = "Welcome Bob",
                ["{{intro.voice}}"] = "en-US-GuyNeural",
                ["and you"] = "and you",
            }
        );
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(Outcome());

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(
                (
                    "segments",
                    new object[]
                    {
                        new
                        {
                            text = "Welcome {{user}}",
                            voice = "{{intro.voice}}",
                            rate = -20,
                            pitch = 10,
                            breakAfterMs = 600,
                        },
                        new { text = "and you" },
                    }
                )
            )
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Segments != null
                    && r.Segments.Count == 2
                    && r.Segments[0]
                        == new TtsSpeakSegment("Welcome Bob", "en-US-GuyNeural", -20, 10, 600)
                    && r.Segments[1] == new TtsSpeakSegment("and you", null, null, null, 0)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_SegmentsGivenAsJsonText_AreParsedLikeAnArray()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("resolved");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(Outcome());

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("segments", "[{\"text\":\"a\",\"breakAfterMs\":300},{\"text\":\"b\"}]"))
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Segments != null && r.Segments.Count == 2 && r.Segments[0].BreakAfterMs == 300
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("[{\"voice\":\"v\"}]")]
    public async Task ExecuteAsync_BadSegments_FailsWithoutDispatch(string segments)
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("resolved");

        ActionResult result = await action.ExecuteAsync(Context(), Action(("segments", segments)));

        result.Succeeded.Should().BeFalse();
        await dispatch
            .DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }
}
