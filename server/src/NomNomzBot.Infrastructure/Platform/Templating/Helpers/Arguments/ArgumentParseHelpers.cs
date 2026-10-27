// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>Reads words as a number, a mention or a duration: one word by position, or every word that fits.</summary>
internal sealed class ArgumentParseHelpers()
    : ArgumentHelperGroup(
        ArgumentRow.Shaped(
            "args.<n>.number",
            "args.number",
            "1",
            key => ArgumentKeys.IsPosition(key, ".number"),
            Number
        ),
        ArgumentRow.Shaped(
            "args.<n>.isNumber",
            "args.isNumber",
            "false",
            key => ArgumentKeys.IsPosition(key, ".isNumber"),
            c => Is(c, ".isNumber", IsNumber)
        ),
        ArgumentRow.Shaped(
            "args.<n>.isMention",
            "args.isMention",
            "true",
            key => ArgumentKeys.IsPosition(key, ".isMention"),
            c => Is(c, ".isMention", IsMention)
        ),
        ArgumentRow.Exact("args.mentions", "@ViewerTwo", c => Join(c, IsMention)),
        ArgumentRow.Exact(
            "args.mentions.count",
            "1",
            c => c.Words.Items.Count(IsMention).ToString(CultureInfo.InvariantCulture)
        ),
        ArgumentRow.Exact("args.numbers", "42 7", c => Join(c, IsNumber)),
        ArgumentRow.Exact("args.duration", "300", Duration)
    )
{
    private static bool IsNumber(string word) => NumberArguments.TryNumber(word, out _);

    private static bool IsMention(string word) => word.Length > 1 && word[0] == '@';

    private static string Join(ArgumentCall call, Func<string, bool> keep) =>
        string.Join(' ', call.Words.Items.Where(keep));

    /// <summary>The n-th word when it is a number, else the default (the only argument) or empty.</summary>
    private static string? Number(ArgumentCall call)
    {
        if (
            !ArgumentKeys.TryPosition(call.Node.Key, ".number", out int n)
            || call.Arguments.Count > 1
        )
            return null;

        string? word = call.Words.At(n);
        if (word is not null && IsNumber(word))
            return word;
        return call.Arguments.Count == 1 ? call.Arguments[0] : string.Empty;
    }

    /// <summary>A yes/no row about the n-th word; a missing word is not a number and not a mention.</summary>
    private static string? Is(ArgumentCall call, string suffix, Func<string, bool> test) =>
        call.Arguments.Count == 0 && ArgumentKeys.TryPosition(call.Node.Key, suffix, out int n)
            ? ArgumentWholeHelpers.Flag(call.Words.At(n) is { } word && test(word))
            : null;

    /// <summary><c>{args.duration:n}</c>: the n-th word read as a duration, in seconds.</summary>
    private static string? Duration(ArgumentCall call) =>
        call.Arguments is [string position]
        && NumberArguments.TryWhole(position, out long n)
        && n is >= 1 and <= int.MaxValue
        && call.Words.At((int)n) is { } word
        && ArgumentDuration.TrySeconds(word, out long seconds)
            ? seconds.ToString(CultureInfo.InvariantCulture)
            : null;
}
