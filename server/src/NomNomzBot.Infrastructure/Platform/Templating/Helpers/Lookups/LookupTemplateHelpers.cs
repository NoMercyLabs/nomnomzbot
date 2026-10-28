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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The <c>lookup</c> namespace (catalog section 7.19): Twitch users, streams and games through the bot's own
/// Helix client, and weather, local time, exchange rates and translation from outside sources. Each outside
/// source sits behind one interface with a timeout, a cache and a fallback string (catalog section 9).
/// </summary>
internal sealed class LookupTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Lazy<LookupSources> _sources;
    private readonly Dictionary<string, LookupRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public LookupTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, null) { }

    /// <summary>The second constructor lets a test register stub sources.</summary>
    internal LookupTemplateHelpers(IServiceScopeFactory scopes, LookupSources? sources)
    {
        _scopes = scopes;
        _sources = new Lazy<LookupSources>(() => sources ?? LookupSources.FromServices(scopes));

        List<TemplateHelperEntry> entries = [];
        foreach (LookupRow row in LookupGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "lookup";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (!_rows.TryGetValue(node.Key, out LookupRow? row))
            return null;

        LookupSources sources = _sources.Value;
        LookupCall call = new(
            LookupData.For(scope, _scopes, sources.Cache),
            sources,
            node.Arguments,
            scope,
            cancellationToken
        );
        return await row.RenderAsync(call);
    }
}

internal static class LookupGroups
{
    public static IReadOnlyList<ILookupHelperGroup> All() =>
        [
            .. typeof(LookupGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(ILookupHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (ILookupHelperGroup)Activator.CreateInstance(t)!),
        ];
}
