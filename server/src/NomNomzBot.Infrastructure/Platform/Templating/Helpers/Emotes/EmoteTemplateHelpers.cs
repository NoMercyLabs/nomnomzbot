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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>
/// The <c>emote</c> namespace (catalog section 7.13): random picks, lists and counts of the channel's Twitch,
/// 7TV, BTTV and FFZ emotes, one emote by name, chat usage, and the owner's favorites. A render fetches each
/// emote source once, however many tokens use it. Random rows draw from the injected <see cref="Random"/>.
/// </summary>
internal sealed class EmoteTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly IEmoteChatSource _chat;
    private readonly Random _random;
    private readonly Dictionary<string, EmoteRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public EmoteTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbViewerDirectory(), new DbEmoteChatSource(), Random.Shared) { }

    /// <summary>The second constructor lets a host (a test) register its own directory, chat source and random.</summary>
    public EmoteTemplateHelpers(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        IEmoteChatSource chat,
        Random random
    )
    {
        _scopes = scopes;
        _directory = directory;
        _chat = chat;
        _random = random;

        List<TemplateHelperEntry> entries = [];
        foreach (EmoteRow row in EmoteGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "emote";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || DataFor(scope) is not { } data)
            return;

        EmoteSources needed = nodes
            .Select(n =>
                _rows.TryGetValue(n.Key, out EmoteRow? row) ? row.Sources : EmoteSources.None
            )
            .Aggregate(EmoteSources.None, (all, one) => all | one);
        await data.WarmAsync(needed);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out EmoteRow? row) && DataFor(scope) is { } data
            ? await row.RenderAsync(new EmoteCall(data, node.Arguments, scope, _random))
            : null;

    private EmoteData? DataFor(TemplateRenderScope scope) =>
        EmoteData.For(scope, _scopes, _directory, _chat);
}

internal static class EmoteGroups
{
    public static IReadOnlyList<IEmoteHelperGroup> All() =>
        [
            .. typeof(EmoteGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IEmoteHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IEmoteHelperGroup)Activator.CreateInstance(t)!),
        ];
}
