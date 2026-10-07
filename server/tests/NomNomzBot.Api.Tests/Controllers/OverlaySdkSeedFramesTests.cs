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
/// The join answer's seed frames reach the widget's own handlers, oldest first, marked as a replay with the real
/// time; a seed frame of a type that already arrived live after the join was sent is dropped, because a live
/// frame is always newer than a seed.
/// </summary>
public sealed class OverlaySdkSeedFramesTests
{
    private const string Record =
        "var log = []; function rec(tag) { return function (data, type, meta) { log.push(tag + ':' + data.n + ':' + (meta && meta.replay) + ':' + (meta && meta.occurredAt)); }; }";

    private static object Frame(string type, int n, string at) =>
        new
        {
            eventType = type,
            data = new { n },
            occurredAt = at,
        };

    private static object Join(params object[] seed) =>
        new
        {
            success = true,
            initialState = (object?)null,
            seed,
        };

    [Fact]
    public void Seed_frames_reach_the_handlers_oldest_first_marked_as_a_replay_with_their_real_time()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(
            Record + " NomNomz.on('goal_progress', rec('g')); NomNomz.on('follow', rec('f'));"
        );

        sdk.Complete(
            "join",
            Join(
                Frame("goal_progress", 3, "2026-10-07T12:00:00Z"),
                Frame("follow", 1, "2026-10-07T12:01:00Z"),
                Frame("goal_progress", 5, "2026-10-07T12:02:00Z")
            )
        );

        sdk.Text("log.join('|')")
            .Should()
            .Be(
                "g:3:true:2026-10-07T12:00:00Z|f:1:true:2026-10-07T12:01:00Z|g:5:true:2026-10-07T12:02:00Z"
            );
    }

    [Fact]
    public void A_seed_frame_of_a_type_that_arrived_live_after_the_join_is_dropped_and_other_types_still_seed()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(
            Record + " NomNomz.on('goal_progress', rec('g')); NomNomz.on('follow', rec('f'));"
        );

        sdk.Deliver("WidgetEvent", new { eventType = "goal_progress", data = new { n = 9 } });
        sdk.Complete(
            "join",
            Join(
                Frame("goal_progress", 3, "2026-10-07T12:00:00Z"),
                Frame("follow", 1, "2026-10-07T12:01:00Z")
            )
        );

        sdk.Text("log.join('|')").Should().Be("g:9:false:undefined|f:1:true:2026-10-07T12:01:00Z");
    }

    [Fact]
    public void A_live_event_after_the_seed_is_not_marked_as_a_replay()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(Record + " NomNomz.on('follow', rec('f'));");

        sdk.Complete("join", Join(Frame("follow", 1, "2026-10-07T12:01:00Z")));
        sdk.Deliver("WidgetEvent", new { eventType = "follow", data = new { n = 2 } });

        sdk.Text("log.join('|')").Should().Be("f:1:true:2026-10-07T12:01:00Z|f:2:false:undefined");
    }

    // The join is sent when the SDK loads, so a widget whose code mounts later still has to get its seed.
    [Fact]
    public void A_handler_registered_after_the_join_answer_still_gets_its_seed_frames()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(Record + " NomNomz.on('follow', rec('early'));");

        sdk.Complete(
            "join",
            Join(
                Frame("goal_progress", 3, "2026-10-07T12:00:00Z"),
                Frame("follow", 1, "2026-10-07T12:01:00Z"),
                Frame("goal_progress", 5, "2026-10-07T12:02:00Z")
            )
        );
        sdk.Evaluate("NomNomz.on('goal_progress', rec('late'));");

        sdk.Text("log.join('|')")
            .Should()
            .Be(
                "early:1:true:2026-10-07T12:01:00Z|late:3:true:2026-10-07T12:00:00Z|late:5:true:2026-10-07T12:02:00Z"
            );
    }

    [Fact]
    public void A_late_handler_gets_no_seed_of_a_type_that_arrived_live_after_the_join()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(Record);

        sdk.Complete("join", Join(Frame("follow", 1, "2026-10-07T12:01:00Z")));
        sdk.Deliver("WidgetEvent", new { eventType = "follow", data = new { n = 2 } });
        sdk.Evaluate("NomNomz.on('follow', rec('late'));");

        sdk.Text("log.length").Should().Be("0");
    }

    [Fact]
    public void A_late_any_handler_gets_every_seed_frame_once()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(Record);

        sdk.Complete(
            "join",
            Join(
                Frame("goal_progress", 3, "2026-10-07T12:00:00Z"),
                Frame("follow", 1, "2026-10-07T12:01:00Z")
            )
        );
        sdk.Evaluate(
            "NomNomz.onAny(function (type, data, meta) { log.push(type + ':' + data.n + ':' + meta.replay); });"
        );

        sdk.Text("log.join('|')").Should().Be("goal_progress:3:true|follow:1:true");
    }

    [Fact]
    public void A_join_answer_without_seed_emits_nothing()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(Record + " NomNomz.on('follow', rec('f'));");

        sdk.Complete("join", new { success = true, initialState = (object?)null });

        sdk.Text("log.length").Should().Be("0");
    }
}
