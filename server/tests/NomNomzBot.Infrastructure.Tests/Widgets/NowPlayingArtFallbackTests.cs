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
/// A cover that fails to load (an expired or removed Spotify image) must not draw a broken-image box on stream:
/// the now-playing widget clears the cover, so the music-note glyph takes its place.
/// </summary>
public sealed class NowPlayingArtFallbackTests
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
    public void A_cover_that_fails_to_load_gives_way_to_the_note_glyph()
    {
        string source = Source();

        source
            .Should()
            .Contain(
                "<span v-if=\"cfg.showArt\" :ref=\"trackArt\" class=\"nnz-avatar art\" :style=\"{ width: artSize + 'px' }\">\n          <img v-if=\"artUrl\" class=\"art-img\" :src=\"sized(artUrl, 96)\" alt=\"\" @error=\"artUrl = ''\">\n          <span v-else class=\"note nnz-accent-text\">&#9835;</span>\n        </span>",
                "the cover frame keeps its size and holds the note when the picture is gone"
            );
    }
}
