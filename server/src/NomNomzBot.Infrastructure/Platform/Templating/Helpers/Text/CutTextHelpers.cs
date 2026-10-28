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

/// <summary>Cutting to a size: by text elements, by words, and by wrapping onto lines.</summary>
internal sealed class CutTextHelpers()
    : TextHelperGroup(
        new TextHelperRow("text.truncate", "hello…", Truncate),
        new TextHelperRow("text.cut", "hello...", Cut),
        new TextHelperRow("text.cut.words", "hello big…", CutWords),
        new TextHelperRow("text.lines.wrap", "aaa bbb / ccc", WrapLines)
    )
{
    private const string LineJoin = " / ";

    /// <summary><c>{text.truncate:n|t}</c>: at most n elements, then one ellipsis when it cut.</summary>
    private static string? Truncate(IReadOnlyList<string> args) =>
        args.Count == 2 && TextArguments.TryCount(args[0], out _)
            ? TextTransformCatalog.Apply("truncate", args[1], args[0]).Value
            : null;

    /// <summary><c>{text.cut:t|max=n|end=s}</c>: the result, end included, is at most n elements.</summary>
    private static string? Cut(IReadOnlyList<string> args)
    {
        if (
            args.Count == 0
            || !TextArguments.TryCount(TextArguments.Named(args, "max") ?? "", out int max)
        )
            return null;

        List<string> elements = TextArguments.Elements(args[0]);
        if (elements.Count <= max)
            return args[0];

        List<string> end = TextArguments.Elements(
            TextArguments.Literal(TextArguments.Named(args, "end") ?? "…")
        );
        int keep = Math.Max(0, max - end.Count);
        return string.Concat(elements.Take(keep)) + string.Concat(end.Take(max));
    }

    /// <summary><c>{text.cut.words:t|n}</c>: the first n words, then one ellipsis when it cut.</summary>
    private static string? CutWords(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !TextArguments.TryCount(args[1], out int count))
            return null;

        List<string> words = TextArguments.Words(args[0]);
        return words.Count <= count
            ? TextArguments.Join(words)
            : TextArguments.Join(words.Take(count)) + "…";
    }

    /// <summary>
    /// <c>{text.lines.wrap:t|n}</c>: greedy word wrap at n elements per line. A chat message holds one line,
    /// so the lines are joined with <c> / </c>. A word longer than n stays whole on its own line.
    /// </summary>
    private static string? WrapLines(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !TextArguments.TryCount(args[1], out int width) || width == 0)
            return null;

        List<string> lines = [];
        string current = string.Empty;
        foreach (string word in TextArguments.Words(args[0]))
        {
            bool fits =
                current.Length == 0 || TextArguments.Elements(current + " " + word).Count <= width;
            if (fits)
            {
                current = current.Length == 0 ? word : current + " " + word;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0)
            lines.Add(current);
        return string.Join(LineJoin, lines);
    }
}
