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

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The drop game widget draws a theme pack; it holds no art of its own (drop-game spec section 4). Inline SVG or
/// a path to an art file in the widget would tie every channel to one look again. A SOURCE-TEXT guard: the
/// widget is browser code, and its rendered frames are proven by the render sheet.
/// </summary>
public sealed partial class DropGameWidgetNoArtTests
{
    private static string Source() => File.ReadAllText(WidgetAssetPaths.VueFile("drop_game"));

    [GeneratedRegex(
        @"<svg|<path\b|viewBox|\.(?:svg|png|webp|gif|jpe?g|mp3|ogg|wav)\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ArtInCode();

    [GeneratedRegex(@"drop-packs|parachute", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PackNameInCode();

    [Fact]
    public void The_widget_carries_no_inline_art_and_no_art_file_path()
    {
        Match art = ArtInCode().Match(Source());

        art.Success.Should()
            .BeFalse($"the art lives in the pack, but drop_game.vue holds '{art.Value}'");
    }

    [Fact]
    public void The_widget_reads_its_pack_through_the_sdk_by_the_pack_id_setting()
    {
        string source = Source();

        source
            .Should()
            .Contain("NomNomz.data.dropPack(", "the manifest is read through the overlay SDK");
        source
            .Should()
            .Contain(
                "NomNomz.data.dropArt(",
                "the pack's drawings are read through the overlay SDK"
            );
        source.Should().Contain("pack_id", "the channel picks the pack with the pack_id setting");
        PackNameInCode()
            .Match(source)
            .Success.Should()
            .BeFalse("the widget names no pack and no pack route; the setting and the SDK do");
    }
}
