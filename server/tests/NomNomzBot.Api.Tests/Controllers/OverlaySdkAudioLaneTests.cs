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
/// UF·T9: the audio page is one lane. A TTS line and a clip started without a handle never play at the
/// same time, in either order; the lane runs in arrival order. A clip started with a handle is its own
/// slot and never waits. Each test runs the served SDK in Jint and reads which fake audio elements had
/// <c>play()</c> called (<c>playing</c>) and how many plays were started in total.
/// </summary>
public sealed class OverlaySdkAudioLaneTests
{
    private static void Clip(OverlaySdkRuntime page, string name, string? handle = null) =>
        page.Deliver("PlaySound", new { playbackUrl = $"https://x/{name}.mp3", handle });

    private static void Line(OverlaySdkRuntime page, string name) =>
        page.Deliver("TtsSpeak", new { text = name, audioUrl = $"https://x/{name}.mp3" });

    private static string Playing(OverlaySdkRuntime page, int element) =>
        page.Text($"__elements[{element}].playing");

    private static void Ended(OverlaySdkRuntime page, int element) =>
        page.Evaluate($"__elements[{element}].fire('ended')");

    [Fact]
    public void A_tts_line_that_arrives_during_a_clip_waits_for_the_clip_to_end()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "clip");
        Line(page, "line");

        Playing(page, 0).Should().Be("true");
        Playing(page, 1).Should().Be("false", "the line waits for the clip");
        page.AudioPlays.Should().Be(1);

        Ended(page, 0);

        Playing(page, 1).Should().Be("true", "the clip ended, so the line starts");
        page.AudioPlays.Should().Be(2);
    }

    [Fact]
    public void A_clip_that_arrives_during_a_tts_line_waits_for_the_line_to_end()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "line");
        Clip(page, "clip");

        Playing(page, 0).Should().Be("true");
        Playing(page, 1).Should().Be("false", "the clip waits for the line");
        page.AudioPlays.Should().Be(1);

        Ended(page, 0);

        Playing(page, 1).Should().Be("true", "the line ended, so the clip starts");
        page.AudioPlays.Should().Be(2);
    }

    [Fact]
    public void A_clip_waits_for_every_tts_line_queued_ahead_of_it()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "one");
        Line(page, "two");
        Clip(page, "clip");

        Ended(page, 0);
        Playing(page, 1).Should().Be("true");
        Playing(page, 2).Should().Be("false", "line two is still ahead of the clip");

        Ended(page, 1);
        Playing(page, 2).Should().Be("true");
    }

    [Fact]
    public void The_lane_runs_in_arrival_order_when_lines_and_clips_alternate()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "one");
        Clip(page, "clip");
        Line(page, "two");

        Ended(page, 0);
        Playing(page, 1).Should().Be("true", "the clip arrived before line two");
        Playing(page, 2).Should().Be("false");

        Ended(page, 1);
        Playing(page, 2).Should().Be("true");
    }

    [Fact]
    public void A_failed_clip_does_not_hold_the_lane()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "clip");
        Line(page, "line");
        page.Evaluate("__elements[0].fire('error')");

        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void An_unhandled_clip_that_arrives_during_an_unhandled_clip_replaces_it_at_once()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "first");
        Clip(page, "second");

        Playing(page, 0).Should().Be("false");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void A_replacing_clip_goes_ahead_of_the_lines_waiting_behind_the_clip_it_replaced()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "first");
        Line(page, "line");
        Clip(page, "second");

        Playing(page, 0).Should().Be("false");
        Playing(page, 2).Should().Be("true");
        Playing(page, 1).Should().Be("false", "the line still waits for the clip");

        Ended(page, 2);
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void A_clip_with_a_handle_plays_at_once_during_a_tts_line()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "line");
        Clip(page, "loop", "ambient");

        Playing(page, 0).Should().Be("true");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void A_clip_with_a_handle_never_holds_a_tts_line_back()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "loop", "ambient");
        Line(page, "line");

        Playing(page, 0).Should().Be("true");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void Stop_all_while_a_line_waits_behind_a_clip_starts_the_line()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Clip(page, "clip");
        Line(page, "line");
        page.Deliver("StopSound", new { all = true });

        Playing(page, 0).Should().Be("false");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void Skipping_a_tts_line_starts_the_clip_waiting_behind_it()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "line");
        Clip(page, "clip");
        page.Deliver("TtsQueueControl", new { action = "skip" });

        Playing(page, 0).Should().Be("false");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void Clearing_tts_stops_the_line_and_the_clip_behind_it_still_plays()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "line");
        Clip(page, "clip");
        page.Deliver("TtsQueueControl", new { action = "clear" });

        Playing(page, 0).Should().Be("false");
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void A_paused_tts_line_keeps_a_later_clip_waiting_until_resume()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        Line(page, "line");
        page.Deliver("TtsQueueControl", new { action = "pause" });
        Clip(page, "clip");

        Playing(page, 0).Should().Be("false");
        Playing(page, 1).Should().Be("false", "the paused line still holds the lane");

        page.Deliver("TtsQueueControl", new { action = "resume" });
        Playing(page, 0).Should().Be("true");
        Playing(page, 1).Should().Be("false");

        Ended(page, 0);
        Playing(page, 1).Should().Be("true");
    }

    [Fact]
    public void A_clip_plays_while_tts_is_paused_and_no_line_is_waiting()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Deliver("TtsQueueControl", new { action = "pause" });
        Clip(page, "clip");

        Playing(page, 0).Should().Be("true");
    }
}
