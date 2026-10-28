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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>Picks by position: from the n-th word to the end, a range of words, or the n-th word with a default.</summary>
internal sealed class ArgumentPickHelpers()
    : ArgumentHelperGroup(
        ArgumentRow.Shaped(
            "args.<n>+",
            "args.from",
            "hello big world",
            key => ArgumentKeys.IsPosition(key, "+"),
            From
        ),
        ArgumentRow.Shaped("args.<a>-<b>", "args.range", "hello big", ArgumentKeys.IsRange, Range),
        ArgumentRow.Shaped(
            "args.<n>:<default>",
            "args.word",
            "nobody",
            key => ArgumentKeys.IsPosition(key, string.Empty),
            WordOrDefault
        )
    )
{
    /// <summary>Every word from the n-th on; empty when there are fewer.</summary>
    private static string? From(ArgumentCall call) =>
        ArgumentKeys.TryPosition(call.Node.Key, "+", out int n) && call.Arguments.Count == 0
            ? call.Words.Slice(n, call.Words.Count)
            : null;

    /// <summary>The words from a to b (both counted from 1); empty past the end, bad when a is after b.</summary>
    private static string? Range(ArgumentCall call) =>
        ArgumentKeys.TryRange(call.Node.Key, out int first, out int last)
        && first <= last
        && call.Arguments.Count == 0
            ? call.Words.Slice(first, last)
            : null;

    /// <summary>The n-th word, or the default (the only argument) when the command has fewer words.</summary>
    private static string? WordOrDefault(ArgumentCall call)
    {
        if (!ArgumentKeys.TryPosition(call.Node.Key, string.Empty, out int n))
            return null;

        return call.Words.At(n) ?? (call.Arguments.Count == 1 ? call.Arguments[0] : null);
    }
}
