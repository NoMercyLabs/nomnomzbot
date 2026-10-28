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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>A set of <c>stream.*</c> or <c>channel.*</c> rows that read one source.</summary>
internal interface IStreamHelperGroup
{
    /// <summary>The template namespace of every row of the group (<c>stream</c> or <c>channel</c>).</summary>
    string Namespace { get; }

    IReadOnlyList<StreamHelperRow> Rows { get; }
}

internal abstract class StreamHelperGroup(string ns, params StreamHelperRow[] rows)
    : IStreamHelperGroup
{
    public string Namespace { get; } = ns;

    public IReadOnlyList<StreamHelperRow> Rows { get; } = rows;
}
