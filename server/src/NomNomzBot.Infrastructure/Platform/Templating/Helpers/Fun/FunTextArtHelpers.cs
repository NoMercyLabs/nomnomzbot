// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
