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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>
/// The <c>list.*</c> template namespace (catalog section 7.8). It claims only the keys of its own rows, so the
/// as-built <c>{list.pick.&lt;name&gt;}</c> prefix keeps resolving through the legacy pre-pass. A singleton:
/// each read opens its own scope for the scoped pick-list service.
/// </summary>
public sealed class ListTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Dictionary<string, IListHelperGroup> _groupByKey = new(
        StringComparer.OrdinalIgnoreCase
    );

    public ListTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, DiscoverGroups()) { }

    internal ListTemplateHelpers(IServiceScopeFactory scopes, IEnumerable<IListHelperGroup> groups)
    {
        _scopes = scopes;
        List<TemplateHelperEntry> entries = [];
        foreach (IListHelperGroup group in groups)
        {
            foreach (TemplateHelperEntry entry in group.Entries)
            {
                _groupByKey.Add(entry.Key, group);
                entries.Add(entry);
            }
        }
        Entries = entries;
    }

    public string Namespace => "list";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _groupByKey.TryGetValue(node.Key, out IListHelperGroup? group)
            ? group.TryRenderAsync(
                node,
                new ListSource(_scopes, scope.BroadcasterId),
                cancellationToken
            )
            : ValueTask.FromResult<string?>(null);

    private static IEnumerable<IListHelperGroup> DiscoverGroups() =>
        typeof(ListTemplateHelpers)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(IListHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IListHelperGroup)Activator.CreateInstance(t)!);
}
