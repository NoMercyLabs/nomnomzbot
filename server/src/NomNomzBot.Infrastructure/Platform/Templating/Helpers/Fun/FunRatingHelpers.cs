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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// The hashed rows: <c>fun.love</c>, <c>fun.rate</c> and <c>fun.percent</c> give the same number for the same
/// inputs all day (<see cref="FunHash"/>), so asking twice never changes the answer. <c>fun.ship</c> blends two names.
/// </summary>
internal sealed class FunRatingHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.love", "73%", Love),
        FunRow.Pure("fun.rate", "8/10", Rate),
        FunRow.Pure("fun.percent", "64%", Percent, TemplateHelperScope.Viewer),
        FunRow.Pure("fun.ship", "ViewerOne + ViewerTwo = ViewTwo", Ship)
    )
{
    private const int PercentSteps = 101;
    private const int RateMax = 10;

    /// <summary><c>love:a|b</c>: one score for the pair, the same whichever name comes first.</summary>
    private static string? Love(FunCall call) =>
        Pair(call) is (string first, string second)
            ? Score(
                call.Scope,
                "love",
                [.. new[] { first, second }.Order(StringComparer.OrdinalIgnoreCase)]
            )
            : null;

    private static string? Rate(FunCall call) =>
        Single(call) is string thing
            ? (FunHash.Below(call.Scope, RateMax, "rate", thing) + 1).ToString(
                CultureInfo.InvariantCulture
            )
                + "/"
                + RateMax.ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary><c>percent:thing</c>: stable per viewer, thing and day.</summary>
    private static string? Percent(FunCall call) =>
        Single(call) is string thing
            ? Score(call.Scope, "percent", Viewer(call.Scope), thing)
            : null;

    /// <summary><c>ship:a|b</c>: the first half of the first name and the last third of the second.</summary>
    private static string? Ship(FunCall call)
    {
        if (Pair(call) is not (string first, string second))
            return null;

        List<string> head = TextArguments.Elements(first);
        List<string> tail = TextArguments.Elements(second);
        int tailLength = (tail.Count + 2) / 3;
        string blend =
            string.Concat(head.Take(head.Count / 2))
            + string.Concat(tail.Skip(tail.Count - tailLength));
        return $"{first} + {second} = {blend}";
    }

    private static string Score(TemplateRenderScope scope, string kind, params string[] parts) =>
        FunHash.Below(scope, PercentSteps, [kind, .. parts]).ToString(CultureInfo.InvariantCulture)
        + "%";

    /// <summary>Two names, in the same order every time, so <c>a|b</c> and <c>b|a</c> hash alike.</summary>
    private static (string, string)? Pair(FunCall call) =>
        call.Args.Count == 2 && call.Args[0].Length > 0 && call.Args[1].Length > 0
            ? (call.Args[0], call.Args[1])
            : null;

    private static string? Single(FunCall call) =>
        call.Args.Count == 1 && call.Args[0].Length > 0 ? call.Args[0] : null;

    private static string Viewer(TemplateRenderScope scope) =>
        scope.Variables.GetValueOrDefault("user.login")
        ?? scope.Variables.GetValueOrDefault("user")
        ?? string.Empty;
}
