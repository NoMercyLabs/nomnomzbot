// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The now-playing widget INTERPOLATES its progress bar between pushes, and interpolation is a claim about
/// something nobody is currently observing. Two rules keep that claim honest, and both are one careless edit
/// from being lost.
///
/// <para>
/// This is a SOURCE-TEXT guard, not a behavioural test — the ticker is browser JavaScript and this suite
/// cannot run it. It proves the decision is still written down, not that a rendered overlay behaves. Read it
/// as "nobody reverted this", never as "the bar is correct on stream".
/// </para>
/// </summary>
public sealed class NowPlayingInterpolationGuardTests
{
    private static string Source()
    {
        const string resourceName =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.now_playing.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the now-playing widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Progress_is_measured_against_a_clock_rather_than_counted_in_timer_ticks()
    {
        // `progressMs += 100` every 100 ms assumes each callback lands exactly on time. An OBS browser
        // source in a hidden scene gets throttled, the callbacks arrive late and sparsely, and the bar falls
        // progressively further behind the real track the longer it plays.
        string source = Source();

        // Checking merely that performance.now() appears SOMEWHERE is not enough — it is also used to stamp
        // the anchor, so a ticker rewritten to count steps again leaves that call untouched and the guard
        // green. This pins the actual computation: elapsed time since the anchor.
        source
            .Should()
            .Contain(
                "performance.now() - baseAtMs",
                "the tick must compute elapsed real time since the last push, not a count of callbacks"
            );
        source
            .Should()
            .Contain(
                "baseProgressMs + elapsed",
                "progress must be the anchor plus measured elapsed time"
            );
        source
            .Should()
            .NotContain(
                "progressMs.value + 100",
                "adding a fixed step per callback, in any spelling, is the drift this guard exists to prevent"
            );
    }

    [Fact]
    public void The_bar_stops_at_the_end_of_the_track_instead_of_running_past_it()
    {
        // Counting up forever means that once a track finishes the widget keeps asserting it is still
        // playing, with a full bar, until the next push happens to arrive — stating something it cannot
        // know. Stopping at the duration says "done" and waits to be told what is next.
        Source()
            .Should()
            .Contain(
                "next >= durationMs.value",
                "the ticker must stop at the track duration rather than interpolating past the end"
            );
    }
}
