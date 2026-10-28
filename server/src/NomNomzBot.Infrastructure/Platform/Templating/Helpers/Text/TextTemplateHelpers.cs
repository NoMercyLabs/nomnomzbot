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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// The one provider of the <c>text</c> namespace (catalog section 7.3). It owns no helper itself: it finds every
/// <see cref="ITextHelperGroup"/> by reflection, merges their rows and sends each node to the group that owns
/// its key. All text helpers are pure, so there is nothing to prepare.
/// </summary>
public sealed class TextTemplateHelpers : ITemplateHelperProvider
{
    private readonly Dictionary<string, ITextHelperGroup> _groupByKey = new(
        StringComparer.OrdinalIgnoreCase
    );

    public TextTemplateHelpers()
        : this(DiscoverGroups()) { }

    internal TextTemplateHelpers(IEnumerable<ITextHelperGroup> groups)
    {
        List<TemplateHelperEntry> entries = [];
        foreach (ITextHelperGroup group in groups)
        {
            foreach (TemplateHelperEntry entry in group.Entries)
            {
                _groupByKey.Add(entry.Key, group);
                entries.Add(entry);
            }
        }
        Entries = entries;
    }

    public string Namespace => "text";

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
            _groupByKey.TryGetValue(node.Key, out ITextHelperGroup? group)
                ? group.TryRender(node, scope)
                : null
        );

    private static IEnumerable<ITextHelperGroup> DiscoverGroups() =>
        typeof(TextTemplateHelpers)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(ITextHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (ITextHelperGroup)Activator.CreateInstance(t)!);
}
