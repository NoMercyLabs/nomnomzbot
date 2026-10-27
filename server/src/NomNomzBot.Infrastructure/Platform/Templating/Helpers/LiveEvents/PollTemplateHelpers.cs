// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The <c>poll.*</c> helpers: the Twitch poll running now and the last finished one.</summary>
internal sealed class PollTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Dictionary<string, PollRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public PollTemplateHelpers(IServiceScopeFactory scopes)
    {
        _scopes = scopes;

        List<TemplateHelperEntry> entries = [];
        foreach (PollRow row in PollGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "poll";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>Starts the polls fetch and the history fetch the render's tokens need, side by side.</summary>
    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || scope.BroadcasterId is null)
            return;

        LivePollData data = LivePollData.For(scope, _scopes);
        await Task.WhenAll(
            nodes
                .Where(n => _rows.ContainsKey(n.Key))
                .Select(n => _rows[n.Key].Warm(new PollCall(n, scope, data, cancellationToken)))
        );
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        scope.BroadcasterId is not null && _rows.TryGetValue(node.Key, out PollRow? row)
            ? await row.Render(
                new PollCall(node, scope, LivePollData.For(scope, _scopes), cancellationToken)
            )
            : null;
}

internal static class PollGroups
{
    public static IReadOnlyList<IPollHelperGroup> All() =>
        [
            .. typeof(PollGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IPollHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IPollHelperGroup)Activator.CreateInstance(t)!),
        ];
}
