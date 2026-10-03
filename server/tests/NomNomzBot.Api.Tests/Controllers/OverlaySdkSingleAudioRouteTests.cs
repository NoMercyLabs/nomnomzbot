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
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Every sound clip and TTS line plays on exactly one overlay page. The server picks that page and sends it the
/// raw hub target; every other page gets widget events for visuals only. These run the served SDK in Jint and
/// count what the page actually plays, so a page that played a widget event would double the sound.
/// </summary>
public sealed class OverlaySdkSingleAudioRouteTests
{
    [Fact]
    public void A_tts_widget_event_with_audio_plays_nothing()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver(
            "WidgetEvent",
            new
            {
                eventType = "tts_speak",
                data = new { text = "hi", audioUrl = "data:audio/mpeg;base64,AQID" },
            }
        );

        page.AudioPlays.Should().Be(0);
        page.BrowserSpeeches.Should().Be(0);
    }

    [Fact]
    public void A_play_sound_widget_event_plays_nothing()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver(
            "WidgetEvent",
            new { eventType = "play_sound", data = new { playbackUrl = "https://x/clip.mp3" } }
        );

        page.AudioPlays.Should().Be(0);
    }

    [Fact]
    public void A_raw_play_sound_plays_exactly_one_clip()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver("PlaySound", new { playbackUrl = "https://x/clip.mp3", volume = 80 });

        page.AudioPlays.Should().Be(1);
    }

    [Fact]
    public void A_raw_tts_line_without_audio_is_spoken_exactly_once()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver(
            "TtsSpeak",
            new
            {
                text = "hello",
                voiceId = "v1",
                provider = "browser",
            }
        );

        page.BrowserSpeeches.Should().Be(1);
        page.AudioPlays.Should().Be(0);
    }

    [Fact]
    public void A_raw_tts_line_with_audio_plays_one_element_and_does_not_use_the_browser_voice()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver("TtsSpeak", new { text = "hello", audioUrl = "data:audio/mpeg;base64,AQID" });

        page.AudioPlays.Should().Be(1);
        page.BrowserSpeeches.Should().Be(0);
    }

    [Fact]
    public void The_page_joins_with_the_sdk_version_it_was_served_with()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.SentFrames.Should().Contain("\"target\":\"JoinWidgetWithSdk\"");
        page.SentFrames.Should().Contain($"\"{OverlaySdkController.Version}\"");
        page.SentFrames.Should().NotContain("__SDK_VERSION__");
    }
}
