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
/// The live-game engine sends a <c>cancelled</c> frame (as <c>game.resolved</c>) when a round is cancelled
/// (<c>LiveGameEngine.CancelInternalAsync</c>). The heist widget had no branch for it, so the lobby panel stayed
/// on screen forever. A SOURCE-TEXT guard: the widget is browser JavaScript and this suite cannot run it.
/// </summary>
public sealed class HeistWidgetCancelledGuardTests
{
    private static string Source()
    {
        const string resourceName = "NomNomzBot.Infrastructure.Content.Widgets.Assets.heist.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the heist widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_cancelled_frame_sets_the_cancelled_phase_keeps_the_reason_and_schedules_the_hide()
    {
        string source = Source();

        source
            .Should()
            .MatchRegex(
                @"if \(d\.kind === 'cancelled'\) \{\s*phase\.value = 'cancelled'\s*cancelReason\.value = d\.reason\s*scheduleHide\(\)",
                "a cancelled round must leave the lobby phase and hide the panel after hideAfterMs"
            );
        source
            .Should()
            .Contain(
                "ref<'lobby' | 'resolved' | 'cancelled'>",
                "the phase type must admit the cancelled phase"
            );
    }

    [Fact]
    public void The_cancelled_phase_shows_its_own_title_and_reason_instead_of_the_lobby_text()
    {
        string source = Source();

        source.Should().Contain("phase === 'cancelled'");
        source.Should().Contain("Heist — cancelled");
        source.Should().Contain("cancelReason");
    }
}
