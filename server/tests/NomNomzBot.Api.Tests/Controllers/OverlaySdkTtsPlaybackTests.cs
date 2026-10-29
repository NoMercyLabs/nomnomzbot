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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// TTS must be audible on stream. The browser's own voice engine is not captured by OBS, so the SDK plays only
/// the server-synthesised audio through a media element and never speaks locally.
/// <para>
/// The SDK is inline script served as text, so these assert on the served script. That is the only place
/// the behaviour exists, and a regression here is invisible to every other test in the suite.
/// </para>
/// </summary>
public sealed class OverlaySdkTtsPlaybackTests
{
    private static string Sdk()
    {
        OverlaySdkController controller = new()
        {
            // The action writes a cache header, so it needs a response to write it to.
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult result = (ContentResult)controller.Get();
        return result.Content!;
    }

    private static string SpeakTtsBody()
    {
        string sdk = Sdk();
        int start = sdk.IndexOf("function speakTts(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the SDK must still have a TTS entry point");
        int next = sdk.IndexOf("function dispatch(", start, StringComparison.Ordinal);
        return sdk[start..(next > start ? next : sdk.Length)];
    }

    [Fact]
    public void Served_audio_is_played_through_a_media_element_that_obs_can_capture()
    {
        string speak = SpeakTtsBody();

        speak
            .Should()
            .Contain("payload.audioUrl", "the synthesised audio is what the stream must hear");
        // A media element is what OBS's browser source records; speechSynthesis is not.
        speak.Should().Contain("createElement(\"audio\")");
    }

    [Fact]
    public void The_overlay_never_uses_the_browser_voice_and_drops_a_line_without_audio()
    {
        string sdk = Sdk();
        string speak = SpeakTtsBody();

        sdk.Should().NotContain("speechSynthesis");
        sdk.Should().NotContain("SpeechSynthesisUtterance");
        // A line with no audio URL is reported to the bot and dropped, never spoken locally.
        speak.Should().Contain("report(\"tts line without audio dropped\")");
    }

    [Fact]
    public void Utterances_are_queued_so_two_voices_never_talk_over_each_other()
    {
        string sdk = Sdk();

        // A busy chat dispatches several utterances within a second; overlapping playback is unintelligible.
        sdk.Should().Contain("ttsQueue");
        sdk.Should().Contain("playNextTts");
        // The queue has to advance on failure too, or one blocked utterance stalls TTS for the whole stream.
        sdk.Should().Contain("tts playback blocked");
    }

    [Fact]
    public void The_dashboard_queue_control_command_is_wired_to_its_own_dispatch_target()
    {
        string sdk = Sdk();

        // TtsConfigController's playback/* endpoints push a raw TtsQueueControl target — it must reach
        // a real handler, not silently no-op through the dispatch() default case.
        sdk.Should().Contain("case \"TtsQueueControl\": ttsQueueControl(");
        sdk.Should().Contain("function ttsQueueControl(data)");
    }

    [Fact]
    public void Pause_stops_advancing_the_queue_and_resume_starts_it_again()
    {
        string sdk = Sdk();
        int playNextStart = sdk.IndexOf("function playNextTts()", StringComparison.Ordinal);
        int playNextEnd = sdk.IndexOf("function ttsSkip", StringComparison.Ordinal);
        playNextStart.Should().BeGreaterThan(-1);
        string playNext = sdk[playNextStart..playNextEnd];

        // playNextTts must refuse to advance while paused — otherwise "pause" only stops the CURRENT
        // utterance and the queue keeps draining behind it.
        playNext.Should().Contain("ttsPaused && !el.isClip");

        sdk.Should().Contain("case \"pause\": ttsPause(); break;");
        sdk.Should().Contain("case \"resume\": ttsResume(); break;");

        int resumeStart = sdk.IndexOf("function ttsResume()", StringComparison.Ordinal);
        int resumeEnd = sdk.IndexOf("function ttsQueueControl", StringComparison.Ordinal);
        string resume = sdk[resumeStart..resumeEnd];
        // Resume must flip the flag back AND kick playback again — flipping the flag alone leaves a
        // paused queue stuck forever since nothing else calls playNextTts for it.
        resume.Should().Contain("ttsPaused = false;");
        resume.Should().Contain("playNextTts();");
    }

    [Fact]
    public void Clear_stops_the_current_utterance_and_drops_every_queued_one_behind_it()
    {
        string sdk = Sdk();
        int clearStart = sdk.IndexOf("function ttsClear()", StringComparison.Ordinal);
        int clearEnd = sdk.IndexOf("function ttsPause", StringComparison.Ordinal);
        clearStart.Should().BeGreaterThan(-1);
        string clear = sdk[clearStart..clearEnd];

        clear.Should().Contain(".pause()");
        // A filter (not shift/splice) — clear must drop EVERY queued line, not just the current one; clips stay.
        clear.Should().Contain("ttsQueue = ttsQueue.filter(");
    }

    [Fact]
    public void Skip_advances_past_only_the_current_utterance()
    {
        string sdk = Sdk();
        int skipStart = sdk.IndexOf("function ttsSkip()", StringComparison.Ordinal);
        int skipEnd = sdk.IndexOf("function ttsClear", StringComparison.Ordinal);
        skipStart.Should().BeGreaterThan(-1);
        string skip = sdk[skipStart..skipEnd];

        skip.Should().Contain(".pause()");
        skip.Should().Contain("ttsQueue.shift();");
        // Skip must immediately try the next item — unlike pause, it does not leave the queue stalled.
        skip.Should().Contain("playNextTts();");
    }

    // Stoney 2026-10-29: the pause between two spoken lines felt unnatural, though synthesis is fast. A queued
    // line was only fetched when its turn came, and the next one only started after the "ended" event.
    [Fact]
    public void A_queued_line_is_fully_buffered_while_the_line_before_it_plays()
    {
        string sdk = Sdk();
        string speak = SpeakTtsBody();

        // A new audio element fetches only its metadata by default; it must be told to buffer the whole line.
        speak.Should().Contain("tts.preload = \"auto\"");
        sdk.Should().Contain("el.preload = \"auto\"", "a queued clip is buffered the same way");
    }

    [Fact]
    public void The_next_line_starts_slightly_before_the_current_one_ends_so_no_gap_is_heard()
    {
        string sdk = Sdk();
        int start = sdk.IndexOf("function playNextTts()", StringComparison.Ordinal);
        int end = sdk.IndexOf("function ttsSkip", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        string playNext = sdk[start..end];

        sdk.Should().Contain("var ttsHandoffLeadMs =");
        playNext.Should().Contain("armHandoff(el, advance)");

        int armStart = sdk.IndexOf("function armHandoff(", StringComparison.Ordinal);
        armStart.Should().BeGreaterThan(-1);
        string arm = sdk[armStart..start];

        // Only a spoken line hands off early; a clip owns its own timing. The handoff reuses advance, which
        // is guarded against running twice, so the later "ended" event does not skip a line.
        arm.Should().Contain("el.isClip");
        arm.Should().Contain("el.duration");
        arm.Should().Contain("ttsHandoffLeadMs");
        arm.Should().Contain("advance()");
    }
}
