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

/// <summary>
/// The shared body of the <c>hypetrain</c>, <c>goal</c> and <c>charity</c> providers: each reads the groups of its
/// own namespace and the one memoized data object of its namespace. A singleton: each read opens its own scope.
/// </summary>
public abstract class LiveEventHelperProvider : ITemplateHelperProvider
{
    private readonly Dictionary<string, LiveEventRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    internal LiveEventHelperProvider(string ns)
    {
        Namespace = ns;

        List<TemplateHelperEntry> entries = [];
        foreach (LiveEventRow row in DiscoverGroups(ns).SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace { get; }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The render's one data object of this namespace; null when the render has no channel.</summary>
    private protected abstract ILiveEventData? DataFor(TemplateRenderScope scope);

    /// <summary>Starts every fetch the render's tokens need side by side.</summary>
    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || DataFor(scope) is not { } data)
            return;

        IEnumerable<Task> fetches = nodes
            .Select(n => _rows.GetValueOrDefault(n.Key))
            .OfType<LiveEventRow>()
            .Select(row => row.Warm(data));
        await Task.WhenAll(fetches);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out LiveEventRow? row) && DataFor(scope) is { } data
            ? await row.Render(new LiveEventCall(data, node.Arguments, scope))
            : null;

    private static IEnumerable<ILiveEventHelperGroup> DiscoverGroups(string ns) =>
        typeof(LiveEventHelperProvider)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(ILiveEventHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (ILiveEventHelperGroup)Activator.CreateInstance(t)!)
            .Where(g => g.Namespace == ns);
}

/// <summary>The <c>hypetrain.*</c> template namespace (catalog section 7.10): the current Hype Train.</summary>
public sealed class HypeTrainTemplateHelpers(IServiceScopeFactory scopes)
    : LiveEventHelperProvider("hypetrain")
{
    private protected override ILiveEventData? DataFor(TemplateRenderScope scope) =>
        LiveHypeTrainData.For(scope, scopes);
}

/// <summary>The <c>goal.*</c> template namespace (catalog section 7.10): the channel's creator goals.</summary>
public sealed class GoalTemplateHelpers(IServiceScopeFactory scopes)
    : LiveEventHelperProvider("goal")
{
    private protected override ILiveEventData? DataFor(TemplateRenderScope scope) =>
        LiveGoalData.For(scope, scopes);
}

/// <summary>The <c>charity.*</c> template namespace (catalog section 7.10): the active charity campaign.</summary>
public sealed class CharityTemplateHelpers(IServiceScopeFactory scopes)
    : LiveEventHelperProvider("charity")
{
    private protected override ILiveEventData? DataFor(TemplateRenderScope scope) =>
        LiveCharityData.For(scope, scopes);
}
