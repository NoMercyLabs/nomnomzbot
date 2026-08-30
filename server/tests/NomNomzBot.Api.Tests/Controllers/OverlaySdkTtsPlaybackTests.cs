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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// TTS was audible to the streamer and SILENT on stream. The SDK spoke every utterance through
/// <c>window.speechSynthesis</c>, and OBS does not capture that from a browser source — the browser's own
/// voice engine plays straight out of the system audio device, bypassing the scene's audio entirely. The
/// server had already synthesised real audio and put its URL on the payload; the SDK ignored it.
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
    public void The_browser_voice_is_only_a_fallback_and_never_preempts_served_audio()
    {
        string speak = SpeakTtsBody();

        int audioBranch = speak.IndexOf("payload.audioUrl", StringComparison.Ordinal);
        // The actual speaking CALL, not the word "speechSynthesis" — which also appears in prose above it.
        int synthesis = speak.IndexOf("synth.speak(", StringComparison.Ordinal);

        synthesis.Should().BeGreaterThan(-1, "client_edge still needs the browser voice");
        // Order is the behaviour: the served-audio branch must be taken, and returned from, first.
        audioBranch.Should().BeLessThan(synthesis);
        speak[audioBranch..synthesis].Should().Contain("return");
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

        // TtsConfigController's playback/* endpoints push a "tts_queue_control" WidgetEvent — it must reach
        // a real handler, not silently no-op through the dispatch() default case.
        sdk.Should().Contain("tts_queue_control: ttsQueueControl");
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
        playNext.Should().Contain("if (ttsPaused) return;");

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
        // "= []" (not shift/splice) — clear must drop the ENTIRE queue, not just the current utterance.
        clear.Should().Contain("ttsQueue = [];");
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
}
