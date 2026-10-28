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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>
/// The <c>bot</c> namespace (catalog section 7.17): the bot's name, build, uptime, command lists, timers and
/// settings for the channel. Time comes from the render's clock, so one render sees one moment.
/// </summary>
internal sealed class BotTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IBotIdentityDirectory _identity;
    private readonly IBotProcessInfo _process;
    private readonly Dictionary<string, BotRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public BotTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new DbBotIdentityDirectory(), new RuntimeBotProcessInfo()) { }

    /// <summary>The second constructor lets a host (a test) register its own identity and process info.</summary>
    public BotTemplateHelpers(
        IServiceScopeFactory scopes,
        IBotIdentityDirectory identity,
        IBotProcessInfo process
    )
    {
        _scopes = scopes;
        _identity = identity;
        _process = process;

        List<TemplateHelperEntry> entries = [];
        foreach (BotRow row in BotGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "bot";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0)
            return;

        BotSources needed = nodes
            .Select(n => _rows.TryGetValue(n.Key, out BotRow? row) ? row.Sources : BotSources.None)
            .Aggregate(BotSources.None, (all, one) => all | one);
        await DataOf(scope).WarmAsync(needed);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        _rows.TryGetValue(node.Key, out BotRow? row)
            ? await row.RenderAsync(new BotCall(DataOf(scope), scope), node.Arguments)
            : null;

    private BotData DataOf(TemplateRenderScope scope) =>
        BotData.For(scope, _scopes, _identity, _process);
}

internal static class BotGroups
{
    public static IReadOnlyList<IBotHelperGroup> All() =>
        [
            .. typeof(BotGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IBotHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IBotHelperGroup)Activator.CreateInstance(t)!),
        ];
}
