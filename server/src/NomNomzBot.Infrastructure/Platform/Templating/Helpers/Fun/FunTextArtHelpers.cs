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

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// The text art rows: <c>fun.flip:text</c> (a table flip that turns the text upside down), <c>fun.unflip</c>,
/// <c>fun.shrug</c> and <c>fun.lenny</c>. The upside-down map is the one <c>text.upsidedown</c> already owns.
/// </summary>
internal sealed class FunTextArtHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.flip", "(╯°□°)╯︵ ǝlqɐʇ", Flip),
        FunRow.Pure("fun.unflip", Unflip, call => Fixed(call, Unflip)),
        FunRow.Pure("fun.shrug", Shrug, call => Fixed(call, Shrug)),
        FunRow.Pure("fun.lenny", Lenny, call => Fixed(call, Lenny))
    )
{
    private const string Unflip = "┬─┬ノ( º _ ºノ)";
    private const string Shrug = "¯\\_(ツ)_/¯";
    private const string Lenny = "( ͡° ͜ʖ ͡°)";
    private const string FlipFace = "(╯°□°)╯︵ ";

    private static readonly FancyTextHelpers UpsideDown = new();

    private static string? Fixed(FunCall call, string art) => call.Args.Count == 0 ? art : null;

    private static string? Flip(FunCall call) =>
        call.Args.Count == 1
        && call.Args[0].Length > 0
        && UpsideDown.TryRender(
            new HelperNode("text.upsidedown", call.Args, "{text.upsidedown}"),
            call.Scope
        )
            is string turned
            ? FlipFace + turned
            : null;
}
