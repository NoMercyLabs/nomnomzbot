// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>A set of viewer rows that read one source (watch time, points, streaks, ...).</summary>
internal interface IPeopleHelperGroup
{
    IReadOnlyList<PeopleRow> Rows { get; }
}

internal abstract class PeopleHelperGroup(params PeopleRow[] rows) : IPeopleHelperGroup
{
    public IReadOnlyList<PeopleRow> Rows { get; } = rows;
}
