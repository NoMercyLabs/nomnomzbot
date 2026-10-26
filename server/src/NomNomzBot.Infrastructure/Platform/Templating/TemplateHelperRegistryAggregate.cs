// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// The one <see cref="ITemplateHelperRegistry"/>: the static as-built rows of <see cref="TemplateHelperRegistry"/>
/// plus every scanned <see cref="ITemplateHelperProvider"/>'s rows, with one map from every canonical key and
/// alias to its entry. Two claims on one key, alias or namespace fail at start-up, never at render time.
/// </summary>
public sealed class TemplateHelperRegistryAggregate : ITemplateHelperRegistry
{
    /// <summary>The registry with no provider: what the engine knew before providers (tests, hand-built resolvers).</summary>
    public static TemplateHelperRegistryAggregate LegacyOnly { get; } = new([]);

    private readonly Dictionary<string, TemplateHelperEntry> _byKey = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly Dictionary<string, ITemplateHelperProvider> _byNamespace = new(
        StringComparer.OrdinalIgnoreCase
    );

    public TemplateHelperRegistryAggregate(IEnumerable<ITemplateHelperProvider> providers)
    {
        List<TemplateHelperEntry> all = [.. TemplateHelperRegistry.All];
        foreach (
            TemplateHelperEntry entry in TemplateHelperRegistry.All.Where(e => e.Prefix is null)
        )
            _byKey.TryAdd(entry.Key, entry);

        foreach (ITemplateHelperProvider provider in providers)
        {
            if (!_byNamespace.TryAdd(provider.Namespace, provider))
                throw new InvalidOperationException(
                    $"Two template helper providers claim the namespace '{provider.Namespace}'."
                );

            foreach (TemplateHelperEntry entry in provider.Entries)
            {
                foreach (string key in entry.Aliases.Prepend(entry.Key))
                {
                    if (!_byKey.TryAdd(key, entry))
                        throw new InvalidOperationException(
                            $"The template helper key '{key}' of provider '{provider.Namespace}' is already registered."
                        );
                }
                all.Add(entry);
            }
        }

        All = all;
    }

    public IReadOnlyList<TemplateHelperEntry> All { get; }

    public IReadOnlyList<TemplateHelperEntry> ForContext(TemplateHelperContext context) =>
        [.. All.Where(e => e.Contexts.Contains(context))];

    public IReadOnlyList<TemplateHelperEntry> ForContext(
        TemplateHelperContext context,
        string eventType
    ) => TemplateHelperRegistry.ForEvent(ForContext(context), eventType);

    public TemplateHelperEntry? Find(string key) =>
        _byKey.TryGetValue(key, out TemplateHelperEntry? entry)
            ? entry
            : All.FirstOrDefault(e => e.Matches(key));

    public ITemplateHelperProvider? ProviderFor(string key) =>
        _byNamespace.Count > 0
        && _byNamespace.TryGetValue(
            HelperNode.NamespaceOf(key),
            out ITemplateHelperProvider? provider
        )
            ? provider
            : null;
}
