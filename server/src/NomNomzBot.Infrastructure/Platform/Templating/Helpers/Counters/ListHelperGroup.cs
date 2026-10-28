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
