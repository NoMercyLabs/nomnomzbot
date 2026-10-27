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

    private static string? Countdown(FunCall call) =>
        call.Args.Count == 1
        && FunDiceSpec.TryWhole(call.Args[0], out int from)
        && from is >= 1 and <= MaxCount
            ? string.Join(
                ' ',
                Enumerable
                    .Range(1, from)
                    .Reverse()
                    .Select(n => n.ToString(CultureInfo.InvariantCulture) + "…")
            )
            : null;

    private static string? Meter(FunCall call) =>
        MeterBar.TryRender(new HelperNode("meter", call.Args, "{meter}"), call.Scope);
}
