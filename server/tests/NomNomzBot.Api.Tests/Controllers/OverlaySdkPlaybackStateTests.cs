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

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Runs the served overlay SDK in Jint and checks the state of the audio elements it creates: the volume a
/// clip is given, and which TTS element plays after a dashboard pause then resume.
/// </summary>
public sealed class OverlaySdkPlaybackStateTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(40, "0.4")]
    [InlineData(250, "1")]
    public void A_clip_plays_at_the_volume_the_server_sent(int volume, string expected)
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver("PlaySound", new { playbackUrl = "https://x/clip.mp3", volume });

        page.Text("__elements[0].volume").Should().Be(expected);
    }

    [Fact]
    public void A_clip_with_no_volume_plays_at_full_volume()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver("PlaySound", new { playbackUrl = "https://x/clip.mp3" });

        page.Text("__elements[0].volume").Should().Be("1");
    }

    [Fact]
    public void Pause_then_resume_then_end_starts_only_the_next_line_and_keeps_the_third_waiting()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();
        foreach (string clip in new[] { "a", "b", "c" })
        {
            page.Deliver(
                "TtsSpeak",
                new
                {
                    text = clip,
                    provider = "self",
                    audioUrl = $"https://x/{clip}.mp3",
                }
            );
        }

        page.Text("__elements.length").Should().Be("3");
        page.Text("__elements[0].playing").Should().Be("true");

        page.Deliver("TtsQueueControl", new { action = "pause" });
        page.Deliver("TtsQueueControl", new { action = "resume" });
        page.Evaluate("__elements[0].fire('ended')");

        page.Text("__elements[1].playing").Should().Be("true", "the second line is next");
        page.Text("__elements[2].playing").Should().Be("false", "the third line must wait");
    }
}
