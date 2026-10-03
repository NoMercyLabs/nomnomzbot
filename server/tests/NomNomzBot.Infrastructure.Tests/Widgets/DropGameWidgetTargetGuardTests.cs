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
/// DropGame opens a round on a random target from 0 to 100 (<c>DropGame.cs</c>), so 0 is a real target.
/// <c>Number(d.target) || 50</c> drew that round's zone at 50 while the game scored against 0. A
/// SOURCE-TEXT guard: the widget is browser JavaScript and this suite cannot run it.
/// </summary>
public sealed class DropGameWidgetTargetGuardTests
{
    private static string Source()
    {
        const string resourceName =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.drop_game.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the drop-game widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_target_of_zero_is_drawn_at_zero_rather_than_replaced_by_the_default()
    {
        string source = Source();

        source
            .Should()
            .NotMatchRegex(
                @"Number\(d\.target\)\s*\|\|",
                "`||` replaces a target of 0, a valid track position, with the default"
            );
        source
            .Should()
            .Contain(
                "target.value = d.target ?? 50",
                "`??` falls back to the default only when the typed frame carries no target at all"
            );
        source
            .Should()
            .Contain(
                "if (d.target != null) target.value = d.target",
                "a results frame without a target leaves the drawn zone alone, and a target of 0 is kept"
            );
    }
}
