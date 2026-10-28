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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>
/// The shared body of the <c>stream</c> and <c>channel</c> providers: each reads the groups of its own namespace.
/// A provider handles only the keys of its own rows; every other key of the namespace (<c>stream.title</c>,
/// <c>channel.display</c>, ...) still goes to the as-built helpers. A singleton: each read opens its own scope.
/// </summary>
public abstract class StreamHelperProvider : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Dictionary<string, StreamHelperRow> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal StreamHelperProvider(IServiceScopeFactory scopes, string ns)
    {
        _scopes = scopes;
        Namespace = ns;

        List<TemplateHelperEntry> entries = [];
        foreach (StreamHelperRow row in DiscoverGroups(ns).SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace { get; }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>
    /// Starts every fetch the render's tokens need side by side: one stats call for the union of their
    /// metrics, one fetch per other source. A later render of a token finds the kept result.
    /// </summary>
    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || scope.BroadcasterId is null)
            return;

        StreamHelperData data = StreamHelperData.For(scope, _scopes);
        List<Task> fetches = [];
        HashSet<string> metrics = [];
        foreach (HelperNode node in nodes)
        {
            if (!_rows.TryGetValue(node.Key, out StreamHelperRow? row))
                continue;

            if (row.Metric is not null)
                metrics.Add(row.Metric);
            fetches.Add(row.Warm(new StreamHelperCall(node, scope, data, cancellationToken)));
        }
        if (metrics.Count > 0)
            fetches.Add(data.StatsAsync(metrics, cancellationToken));

        await Task.WhenAll(fetches);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out StreamHelperRow? row)
            ? await row.Render(
                new StreamHelperCall(
                    node,
                    scope,
                    StreamHelperData.For(scope, _scopes),
                    cancellationToken
                )
            )
            : null;

    private static IEnumerable<IStreamHelperGroup> DiscoverGroups(string ns) =>
        typeof(StreamHelperProvider)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(IStreamHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IStreamHelperGroup)Activator.CreateInstance(t)!)
            .Where(g => g.Namespace == ns);
}

/// <summary>The <c>stream.*</c> template namespace (catalog section 7.6): the live stream, its stats and history.</summary>
public sealed class StreamTemplateHelpers(IServiceScopeFactory scopes)
    : StreamHelperProvider(scopes, "stream");

/// <summary>The <c>channel.*</c> template namespace (catalog section 7.6): the channel's settings, counts and ties.</summary>
public sealed class ChannelTemplateHelpers(IServiceScopeFactory scopes)
    : StreamHelperProvider(scopes, "channel");
