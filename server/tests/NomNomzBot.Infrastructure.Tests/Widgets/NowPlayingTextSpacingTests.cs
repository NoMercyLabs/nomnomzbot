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

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A song with no requester left an empty hidden row above the progress bar, so the text sat high and a hole
/// opened under it. The rows now spread evenly over a height that stays the same for every song.
/// </summary>
public sealed class NowPlayingTextSpacingTests
{
    private static string Source()
    {
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(
                "NomNomzBot.Infrastructure.Content.Widgets.Assets.now_playing.vue"
            );
        stream.Should().NotBeNull("the now-playing widget must ship as an embedded asset");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_song_without_a_requester_drops_the_row_instead_of_hiding_it()
    {
        string source = Source();

        source.Should().Contain("<div v-if=\"requestedBy\" class=\"nnz-sub requester\">");
        source
            .Should()
            .NotContain(
                "visibility: 'hidden'",
                "a hidden row still takes its space and leaves a hole"
            );
    }

    [Fact]
    public void The_visible_rows_spread_evenly_over_a_fixed_height()
    {
        string source = Source();

        source.Should().Contain("<div class=\"sizer\" aria-hidden=\"true\">");
        source.Should().Contain("justify-content: space-evenly;");
    }
}
