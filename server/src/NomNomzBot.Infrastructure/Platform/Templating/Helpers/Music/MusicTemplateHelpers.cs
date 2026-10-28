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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>
/// The <c>music</c> namespace (catalog section 7.9): now playing, progress, the song-request queue, history
/// and the request settings. A render fetches each music source once, however many tokens use it.
/// </summary>
internal sealed class MusicTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly Dictionary<string, MusicRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public MusicTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory()) { }

    /// <summary>The second constructor lets a host (a test) register its own <see cref="IViewerDirectory"/>.</summary>
    public MusicTemplateHelpers(IServiceScopeFactory scopes, IViewerDirectory directory)
    {
        _scopes = scopes;
        _directory = directory;

        List<TemplateHelperEntry> entries = [];
        foreach (MusicRow row in MusicGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "music";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || MusicData.For(scope, _scopes, _directory) is not { } data)
            return;

        MusicSources needed = nodes
            .Select(n =>
                _rows.TryGetValue(n.Key, out MusicRow? row) ? row.Sources : MusicSources.None
            )
            .Aggregate(MusicSources.None, (all, one) => all | one);
        await data.WarmAsync(needed);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out MusicRow? row)
        && MusicData.For(scope, _scopes, _directory) is { } data
            ? await row.RenderAsync(new MusicCall(data, node.Arguments, scope, cancellationToken))
            : null;
}

internal static class MusicGroups
{
    public static IReadOnlyList<IMusicHelperGroup> All() =>
        [
            .. typeof(MusicGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IMusicHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IMusicHelperGroup)Activator.CreateInstance(t)!),
        ];
}
