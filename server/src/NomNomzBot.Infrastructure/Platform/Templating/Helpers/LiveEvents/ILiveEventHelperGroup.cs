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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal interface ILiveEventHelperGroup
{
    /// <summary>The template namespace of every row of the group (<c>hypetrain</c>, <c>goal</c> or <c>charity</c>).</summary>
    string Namespace { get; }

    IReadOnlyList<LiveEventRow> Rows { get; }
}

internal abstract class LiveEventHelperGroup(string ns, params LiveEventRow[] rows)
    : ILiveEventHelperGroup
{
    public string Namespace { get; } = ns;

    public IReadOnlyList<LiveEventRow> Rows { get; } = rows;
}
