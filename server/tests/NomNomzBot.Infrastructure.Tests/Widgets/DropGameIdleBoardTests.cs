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

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// An idle drop-game lobby must not cover other widgets (the chat box): no hint text, and the board card
/// only exists once there is something on it. A SOURCE-TEXT guard: the widget is browser code.
/// </summary>
public sealed class DropGameIdleBoardTests
{
    private static string Source() => File.ReadAllText(WidgetAssetPaths.VueFile("drop_game"));

    [Fact]
    public void The_widget_carries_no_how_to_play_hint()
    {
        Source().Should().NotContain("Type !drop to play");
    }

    [Fact]
    public void The_board_card_exists_only_with_rows_and_sits_at_its_own_anchor()
    {
        Source()
            .Should()
            .MatchRegex(
                @"<div\s+v-if=""boardRows\.length""\s+class=""nnz-card nnz-placed board""\s+:data-anchor=""cfg\.boardAnchor""",
                "an empty board, or one pinned over the chat corner, would cover other widgets"
            );
    }

    [Fact]
    public void The_stage_draws_nothing_until_a_drop_is_in_the_air()
    {
        Source()
            .Should()
            .MatchRegex(
                @"<div\s+v-if=""flights\.length""\s+class=""stage""",
                "a lobby with no drops shows nothing on screen"
            );
    }
}
