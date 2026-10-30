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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Find and replace: all, first, ignoring case, remove. An empty find text is a bad argument.</summary>
internal sealed class ReplaceTextHelpers()
    : TextHelperGroup(
        new TextHelperRow(
            "text.replace",
            "hello chat",
            args => Replace(args, StringComparison.Ordinal, all: true)
        ),
        new TextHelperRow(
            "text.replace.first",
            "b a a",
            args => Replace(args, StringComparison.Ordinal, all: false)
        ),
        new TextHelperRow(
            "text.replace.icase",
            "bye",
            args => Replace(args, StringComparison.OrdinalIgnoreCase, all: true)
        ),
        new TextHelperRow("text.remove", "hell wrld", Remove)
    )
{
    private static string? Replace(
        IReadOnlyList<string> args,
        StringComparison comparison,
        bool all
    )
    {
        if (args.Count != 3 || args[1].Length == 0)
            return null;

        string find = args[1];
        string with = args[2];
        if (all)
            return args[0].Replace(find, with, comparison);

        int index = args[0].IndexOf(find, comparison);
        return index < 0 ? args[0] : args[0][..index] + with + args[0][(index + find.Length)..];
    }

    private static string? Remove(IReadOnlyList<string> args) =>
        args.Count == 2
            ? Replace([args[0], args[1], ""], StringComparison.Ordinal, all: true)
            : null;
}
