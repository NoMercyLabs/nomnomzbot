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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The shared body of the <c>user</c> and <c>target</c> providers: both read the same groups, each for its own
/// side. A provider handles only the keys of its own Entries; every other <c>user.*</c> key (names, pronoun
/// grammar, ages, links) still goes to the as-built helpers.
/// </summary>
internal abstract class PeopleHelperProvider : ITemplateHelperProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly ViewerSide _side;
    private readonly Dictionary<string, PeopleRow> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Prefix, PeopleRow Row)> _prefixes = [];

    protected PeopleHelperProvider(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        ViewerSide side,
        string ns
    )
    {
        _scopes = scopes;
        _directory = directory;
        _side = side;
        Namespace = ns;

        ViewerSides flag = side == ViewerSide.User ? ViewerSides.User : ViewerSides.Target;
        List<TemplateHelperEntry> entries = [];
        foreach (PeopleRow row in PeopleGroups.All().SelectMany(g => g.Rows))
        {
            if (!row.Sides.HasFlag(flag))
                continue;

            entries.Add(row.ToEntry(side, ns));
            if (row.IsPrefix)
                _prefixes.Add((ns + "." + row.Suffix, row));
            else
                _exact.Add(ns + "." + row.Suffix, row);
        }
        Entries = entries;
    }

    public string Namespace { get; }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    private string ItemKey => "people." + _side;

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (nodes.Count == 0 || scope.BroadcasterId is not { } broadcasterId)
            return;

        using IServiceScope services = _scopes.CreateScope();
        ViewerRef? who = await _directory.ResolveAsync(
            services.ServiceProvider,
            _side,
            scope.Variables,
            cancellationToken
        );
        if (who is not null)
            scope.Items[ItemKey] = new ViewerData(_scopes, broadcasterId, who);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (!scope.Items.TryGetValue(ItemKey, out object? stored) || stored is not ViewerData data)
            return null;

        (PeopleRow? row, string tail) = Find(node.Key);
        return row is null
            ? null
            : await row.Render(
                new PeopleCall(data, node.Arguments, tail, scope, cancellationToken)
            );
    }

    private (PeopleRow? Row, string Tail) Find(string key)
    {
        if (_exact.TryGetValue(key, out PeopleRow? exact))
            return (exact, string.Empty);

        foreach ((string prefix, PeopleRow row) in _prefixes)
        {
            if (
                key.Length > prefix.Length
                && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            )
                return (row, key[prefix.Length..]);
        }
        return (null, string.Empty);
    }
}

/// <summary>The groups every people provider reads, found once by scan (one parameterless class per source).</summary>
internal static class PeopleGroups
{
    public static IReadOnlyList<IPeopleHelperGroup> All() =>
        [
            .. typeof(PeopleGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IPeopleHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IPeopleHelperGroup)Activator.CreateInstance(t)!),
        ];
}
