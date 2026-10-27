// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>The shared row table of a list group: registry rows out, a render by key in.</summary>
internal abstract class ListHelperGroup : IListHelperGroup
{
    private readonly Dictionary<string, ListHelperRow> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    protected ListHelperGroup(params ListHelperRow[] rows)
    {
        foreach (ListHelperRow row in rows)
            _rows.Add(row.Key, row);
        Entries = [.. rows.Select(r => r.ToEntry())];
    }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public ValueTask<string?> TryRenderAsync(
        HelperNode node,
        ListSource source,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out ListHelperRow? row)
            ? row.Render(new ListHelperCall(node.Arguments, source, cancellationToken))
            : ValueTask.FromResult<string?>(null);
}
