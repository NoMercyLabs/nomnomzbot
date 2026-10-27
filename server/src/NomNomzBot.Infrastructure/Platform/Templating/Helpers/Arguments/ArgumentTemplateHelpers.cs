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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// The one provider of the <c>args</c> namespace (catalog section 7.16). It owns no helper itself: it finds every
/// <see cref="IArgumentHelperGroup"/> by reflection, merges their rows and asks each group in turn to render a
/// node. Every helper reads the command seed (<c>args</c>) and is pure, so there is nothing to prepare.
/// The seed keys <c>args</c> and <c>args.&lt;n&gt;</c> stay with the legacy helpers: a seed always wins.
/// </summary>
public sealed class ArgumentTemplateHelpers : ITemplateHelperProvider
{
    private readonly IReadOnlyList<IArgumentHelperGroup> _groups;

    public ArgumentTemplateHelpers()
        : this(DiscoverGroups()) { }

    internal ArgumentTemplateHelpers(IEnumerable<IArgumentHelperGroup> groups)
    {
        _groups = [.. groups];
        Entries = [.. _groups.SelectMany(g => g.Entries)];
    }

    public string Namespace => "args";

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
    )
    {
        if (!CommandWords.TryRead(scope, out CommandWords? words))
            return ValueTask.FromResult<string?>(null);

        foreach (IArgumentHelperGroup group in _groups)
        {
            string? rendered = group.TryRender(node, words);
            if (rendered is not null)
                return ValueTask.FromResult<string?>(rendered);
        }
        return ValueTask.FromResult<string?>(null);
    }

    private static IEnumerable<IArgumentHelperGroup> DiscoverGroups() =>
        typeof(ArgumentTemplateHelpers)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(IArgumentHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IArgumentHelperGroup)Activator.CreateInstance(t)!);
}
