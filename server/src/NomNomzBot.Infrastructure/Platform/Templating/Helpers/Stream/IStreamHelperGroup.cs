// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
