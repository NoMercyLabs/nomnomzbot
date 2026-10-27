// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
