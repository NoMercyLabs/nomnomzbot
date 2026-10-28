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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>One small group of <c>music.*</c> rows; the provider finds every group by scan.</summary>
internal interface IMusicHelperGroup
{
    IReadOnlyList<MusicRow> Rows { get; }
}

internal abstract class MusicHelperGroup(params MusicRow[] rows) : IMusicHelperGroup
{
    public IReadOnlyList<MusicRow> Rows { get; } = rows;
}
