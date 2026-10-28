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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// The <c>fun</c> namespace (catalog section 7.18): chat games, hashed ratings, random picks and text art. It
/// claims only the keys of its own rows. Every draw comes from one injectable <see cref="Random"/> (the shared
/// one in production, a seeded one in a test). A singleton: the 8-ball opens its own scope for the scoped pick-list
/// service.
/// </summary>
public sealed class FunTemplateHelpers : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Random _random;
    private readonly Dictionary<string, FunRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public FunTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, Random.Shared) { }

    internal FunTemplateHelpers(IServiceScopeFactory scopes, Random random)
    {
        _scopes = scopes;
        _random = random;

        List<TemplateHelperEntry> entries = [];
        foreach (FunRow row in FunGroups.All().SelectMany(g => g.Rows))
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace => "fun";

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
        _rows.TryGetValue(node.Key, out FunRow? row)
            ? row.Render(
                new FunCall(
                    node.Arguments,
                    scope,
                    _random,
                    new ListSource(_scopes, scope.BroadcasterId),
                    cancellationToken
                )
            )
            : ValueTask.FromResult<string?>(null);
}

internal static class FunGroups
{
    public static IReadOnlyList<IFunHelperGroup> All() =>
        [
            .. typeof(FunGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IFunHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IFunHelperGroup)Activator.CreateInstance(t)!),
        ];
}
