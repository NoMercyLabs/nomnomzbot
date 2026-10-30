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

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// <c>fun.countdown:n</c> (the count from n down to 1, at most 20) and <c>fun.meter:value</c>, which is the
/// <c>meter</c> bar under the fun namespace (Stoney's group 17 form) and renders exactly what <c>meter</c> does.
/// </summary>
internal sealed class FunCountdownHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.countdown", "3… 2… 1…", Countdown),
        FunRow.Pure("fun.meter", "██████░░░░ 60%", Meter)
    )
{
    private const int MaxCount = 20;

    private static readonly MeterBarHelpers MeterBar = new();

    private static string? Countdown(FunCall call)
    {
        if (call.Args.Count != 1 || !FunDiceSpec.TryWhole(call.Args[0], out int from))
            return null;

        from = ViewerCountLimit.Within(from, call.Scope.TextCountLimit);
        if (from is < 1 or > MaxCount)
            return null;

        return string.Join(
            ' ',
            Enumerable
                .Range(1, from)
                .Reverse()
                .Select(n => n.ToString(CultureInfo.InvariantCulture) + "…")
        );
    }

    private static string? Meter(FunCall call) =>
        MeterBar.TryRender(new HelperNode("meter", call.Args, "{meter}"), call.Scope);
}
