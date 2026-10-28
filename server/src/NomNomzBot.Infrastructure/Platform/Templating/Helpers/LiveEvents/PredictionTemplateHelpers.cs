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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The <c>prediction.*</c> helpers: the Twitch prediction in progress and the last finished one.</summary>
internal sealed class PredictionTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Dictionary<string, PredictionRow> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    public PredictionTemplateHelpers(IServiceScopeFactory scopes)
    {
        _scopes = scopes;

        List<TemplateHelperEntry> entries = [];
        foreach (PredictionRow row in PredictionGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "prediction";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>Starts the predictions fetch and the history fetch the render's tokens need, side by side.</summary>
    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || scope.BroadcasterId is null)
            return;

        LivePredictionData data = LivePredictionData.For(scope, _scopes);
        await Task.WhenAll(
            nodes
                .Where(n => _rows.ContainsKey(n.Key))
                .Select(n =>
                    _rows[n.Key].Warm(new PredictionCall(n, scope, data, cancellationToken))
                )
        );
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        scope.BroadcasterId is not null && _rows.TryGetValue(node.Key, out PredictionRow? row)
            ? await row.Render(
                new PredictionCall(
                    node,
                    scope,
                    LivePredictionData.For(scope, _scopes),
                    cancellationToken
                )
            )
            : null;
}

internal static class PredictionGroups
{
    public static IReadOnlyList<IPredictionHelperGroup> All() =>
        [
            .. typeof(PredictionGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IPredictionHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IPredictionHelperGroup)Activator.CreateInstance(t)!),
        ];
}
