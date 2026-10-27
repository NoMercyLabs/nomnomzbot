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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>
/// The shared half of the <c>chat</c>, <c>tts</c> and <c>link</c> providers (catalog section 7.14): it takes the
/// rows of its namespace from the groups, fetches each source once per render and renders a node through its
/// row. A render with no channel still renders every pure row.
/// </summary>
internal abstract class ChatNamespaceProvider : ITemplateHelperProvider
{
    private readonly ChatDataSource? _source;
    private readonly Dictionary<string, ChatRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    protected ChatNamespaceProvider(string ns, ChatDataSource? source)
    {
        _source = source;
        Namespace = ns;

        List<TemplateHelperEntry> entries = [];
        foreach (
            ChatRow row in ChatGroups.All().SelectMany(g => g.Rows).Where(r => r.Namespace == ns)
        )
        {
            _rows.Add(row.Key, row);
            entries.Add(row.ToEntry());
        }
        Entries = entries;
    }

    public string Namespace { get; }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        ChatSources needed = nodes
            .Select(n =>
                _rows.TryGetValue(n.Key, out ChatRow? row) ? row.Sources : ChatSources.None
            )
            .Aggregate(ChatSources.None, (all, one) => all | one);
        if (needed != ChatSources.None && DataFor(scope) is { } data)
            await data.WarmAsync(needed);
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (
            !_rows.TryGetValue(node.Key, out ChatRow? row)
            || node.Arguments.Count < row.MinArgs
            || node.Arguments.Count > row.MaxArgs
        )
            return null;

        ChatData? data = row.Sources == ChatSources.None ? null : DataFor(scope);
        return await row.Render(new ChatCall(data, node.Arguments, scope, cancellationToken));
    }

    private ChatData? DataFor(TemplateRenderScope scope) =>
        _source is null ? null : ChatData.For(scope, _source);
}

internal static class ChatGroups
{
    public static IReadOnlyList<IChatHelperGroup> All() =>
        [
            .. typeof(ChatGroups)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(IChatHelperGroup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null
                )
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => (IChatHelperGroup)Activator.CreateInstance(t)!),
        ];
}

/// <summary>One small group of <c>chat.*</c>, <c>tts.*</c> or <c>link.*</c> rows; the providers find every group by scan.</summary>
internal interface IChatHelperGroup
{
    IReadOnlyList<ChatRow> Rows { get; }
}

internal abstract class ChatHelperGroup(params ChatRow[] rows) : IChatHelperGroup
{
    public IReadOnlyList<ChatRow> Rows { get; } = rows;
}
