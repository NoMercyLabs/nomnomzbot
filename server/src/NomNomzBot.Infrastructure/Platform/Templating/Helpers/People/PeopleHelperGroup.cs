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
