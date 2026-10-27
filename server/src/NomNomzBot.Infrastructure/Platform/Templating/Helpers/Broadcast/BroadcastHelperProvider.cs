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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// The shared body of the ad, schedule and activity providers: one namespace over small groups of rows, and one
/// data object per render that every row reads from (each source is fetched once, however many tokens use it).
/// </summary>
internal abstract class BroadcastHelperProvider<TData> : ITemplateHelperProvider
{
    private readonly Dictionary<string, BroadcastRow<TData>> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal BroadcastHelperProvider(IEnumerable<BroadcastHelperGroup<TData>> groups)
    {
        List<TemplateHelperEntry> entries = [];
        foreach (BroadcastRow<TData> row in groups.SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }

        Entries = entries;
    }

    public abstract string Namespace { get; }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The render's data, or null when the render has no channel.</summary>
    protected abstract TData? DataFor(TemplateRenderScope scope);

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out BroadcastRow<TData>? row) && DataFor(scope) is { } data
            ? await row.RenderAsync(new(data, node.Arguments, scope, cancellationToken))
            : null;
}
