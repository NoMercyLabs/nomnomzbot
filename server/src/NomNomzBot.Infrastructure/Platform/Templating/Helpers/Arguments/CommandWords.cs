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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// The arguments of the command a template renders in, read from the <c>args</c> seed that
/// <c>ChatMessageHandler.BuildArgumentVariables</c> builds: the text as written and its words, split on spaces
/// the same way the seed splits them. A render with no <c>args</c> seed has no command, so there is nothing to read.
/// </summary>
internal sealed class CommandWords
{
    private const string SeedKey = "args";

    private CommandWords(string text)
    {
        Text = text.Trim();
        Items = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The argument text, trimmed.</summary>
    public string Text { get; }

    /// <summary>The words, in order.</summary>
    public IReadOnlyList<string> Items { get; }

    public int Count => Items.Count;

    /// <summary>The n-th word (counted from 1), or null when the command has fewer.</summary>
    public string? At(int n) => n >= 1 && n <= Items.Count ? Items[n - 1] : null;

    /// <summary>The words from the first to the last position (counted from 1, both inclusive), joined by a space.</summary>
    public string Slice(int first, int last)
    {
        int from = Math.Max(first, 1) - 1;
        int to = Math.Min(last, Items.Count);
        return from >= to ? string.Empty : string.Join(' ', Items.Skip(from).Take(to - from));
    }

    public static bool TryRead(TemplateRenderScope scope, out CommandWords words)
    {
        words = null!;
        if (!scope.Variables.TryGetValue(SeedKey, out string? text))
            return false;

        words = new CommandWords(text ?? string.Empty);
        return true;
    }
}
