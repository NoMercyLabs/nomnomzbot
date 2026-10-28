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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// The shared half of the three number providers (<c>num</c>, <c>math</c>, <c>meter</c>; catalog section 7.4):
/// it owns no helper itself, merges the rows of its groups and sends each node to the group that owns its key.
/// All number helpers are pure, so there is nothing to prepare.
/// </summary>
public abstract class NumberHelperProvider : ITemplateHelperProvider
{
    private readonly Dictionary<string, INumberHelperGroup> _groupByKey = new(
        StringComparer.OrdinalIgnoreCase
    );

    private protected NumberHelperProvider(string ns, IEnumerable<INumberHelperGroup> groups)
    {
        Namespace = ns;
        List<TemplateHelperEntry> entries = [];
        foreach (INumberHelperGroup group in groups)
        {
            foreach (TemplateHelperEntry entry in group.Entries)
            {
                _groupByKey.Add(entry.Key, group);
                entries.Add(entry);
            }
        }
        Entries = entries;
    }

    public string Namespace { get; }

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
        ValueTask.FromResult(
            _groupByKey.TryGetValue(node.Key, out INumberHelperGroup? group)
                ? group.TryRender(node, scope)
                : null
        );
}
