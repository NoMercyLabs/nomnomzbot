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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;

/// <summary>
/// The include rows (catalog section 7.5): <c>include.command:name</c> and its short form <c>command.name</c>
/// (the reply of another command, see <see cref="CommandInclude"/>) and <c>include.list:name|n</c> (entry n of a
/// pick list, counted from 1). A command or list that does not exist, a bad number, or a render without a channel
/// prints the token as written; an n past the end of a list renders empty, as <c>list.item</c> does.
/// <c>include.builtin</c> is not here: a built-in computes its own reply variables, and which reply slot a
/// bare key means is a decision for Stoney.
/// </summary>
public sealed class IncludeTemplateHelpers(IServiceScopeFactory scopes) : ITemplateHelperProvider
{
    private const string ShortPrefix = "command.";

    private readonly CommandInclude _commands = new(scopes);

    public string Namespace => "include";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        ComposingRows.Row(
            "include.command",
            "ViewerOne sends a hug",
            TemplateHelperFreshness.Memory
        ),
        ComposingRows.Row(
            "command.<name>",
            "ViewerOne sends a hug",
            TemplateHelperFreshness.Memory,
            prefix: ShortPrefix,
            descriptionKey: "template.helper.command.name"
        ),
        ComposingRows.Row("include.list", "cookies", TemplateHelperFreshness.Live, "pick-list"),
    ];

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Key.StartsWith(ShortPrefix, StringComparison.OrdinalIgnoreCase))
            return node.Arguments.Count == 0
                ? await _commands.RenderAsync(
                    scope,
                    node.Key[ShortPrefix.Length..],
                    cancellationToken
                )
                : null;

        return node.Key.ToLowerInvariant() switch
        {
            "include.command" when node.Arguments.Count == 1 && node.Arguments[0].Length > 0 =>
                await _commands.RenderAsync(scope, node.Arguments[0], cancellationToken),
            "include.list" => await ListEntryAsync(scope, node, cancellationToken),
            _ => null,
        };
    }

    private async Task<string?> ListEntryAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (
            scope.BroadcasterId is null
            || node.Arguments.Count != 2
            || !ListText.TryPosition(node.Arguments[1], out int position)
        )
            return null;

        ListSource lists = new(scopes, scope.BroadcasterId);
        IReadOnlyList<string> items = await lists.ItemsAsync(node.Arguments[0], cancellationToken);
        return ListText.ItemAt(items, position);
    }
}
