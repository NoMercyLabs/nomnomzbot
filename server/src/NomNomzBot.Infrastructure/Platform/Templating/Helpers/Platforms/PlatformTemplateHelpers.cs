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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

/// <summary>
/// The <c>platform</c> namespace (catalog section 7.15): one channel, several platform connections, read side
/// by side. A connection that is not made, or not live, prints nothing.
/// </summary>
internal sealed class PlatformTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IPlatformDirectory _directory;
    private readonly Dictionary<string, PlatformRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public PlatformTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbPlatformDirectory()) { }

    /// <summary>The second constructor lets a host (a test) register its own <see cref="IPlatformDirectory"/>.</summary>
    public PlatformTemplateHelpers(IServiceScopeFactory scopes, IPlatformDirectory directory)
    {
        _scopes = scopes;
        _directory = directory;

        List<TemplateHelperEntry> entries = [];
        foreach (PlatformRow row in PlatformGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "platform";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || PlatformData.For(scope, _scopes, _directory) is not { } data)
            return;

        PlatformSources needed = nodes
            .Select(n =>
                _rows.TryGetValue(n.Key, out PlatformRow? row) ? row.Sources : PlatformSources.None
            )
            .Aggregate(PlatformSources.None, (all, one) => all | one);
        await data.WarmAsync(needed);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out PlatformRow? row)
        && PlatformData.For(scope, _scopes, _directory) is { } data
            ? await row.RenderAsync(new PlatformCall(data, node.Arguments, scope))
            : null;
}

internal static class PlatformGroups
{
    public static IReadOnlyList<IPlatformHelperGroup> All() =>
        [
            .. typeof(PlatformGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IPlatformHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IPlatformHelperGroup)Activator.CreateInstance(t)!),
        ];
}
