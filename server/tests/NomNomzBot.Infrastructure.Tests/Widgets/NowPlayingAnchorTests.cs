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
/// The anchor setting puts the now-playing box in the corner the streamer chose. A box pinned to both side edges
/// fills the browser source, so in a wide source "bottom-right" drew the box from the left edge.
/// </summary>
public sealed class NowPlayingAnchorTests
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
    public void The_box_is_not_pinned_to_both_side_edges()
    {
        string source = Source();

        source
            .Should()
            .NotContain(
                "left: var(--nnz-edge);\n  right: var(--nnz-edge);",
                "a box pinned left and right ignores the left and right anchors"
            );
    }

    [Fact]
    public void The_box_has_one_width_capped_by_the_browser_source()
    {
        string source = Source();

        source
            .Should()
            .Contain(
                "  width: min(calc(100% - 2 * var(--nnz-inset)), 616px);\n}",
                "the box keeps one width between songs and still fits a narrow browser source"
            );
    }
}
