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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;

/// <summary>
/// The include rows (catalog section 7.5): <c>include.cmd:name</c> (the reply of another command, see
/// <see cref="CommandInclude"/>), <c>include.builtin:key</c> (the reply of a read-only built-in, see
/// <see cref="BuiltinInclude"/>) and <c>include.list:name|n</c> (entry n of a pick list, counted from 1). A command,
/// built-in or list that does not exist, a bad number, or a render without a channel prints the token as written;
/// an n past the end of a list renders empty, as <c>list.item</c> does. <c>{command.*}</c> is the running command
/// (catalog 7.16) and is never an include.
/// </summary>
public sealed class IncludeTemplateHelpers(IServiceScopeFactory scopes) : ITemplateHelperProvider
{
    private readonly CommandInclude _commands = new(scopes);
    private readonly BuiltinInclude _builtins = new(scopes);

    public string Namespace => "include";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        ComposingRows.Row("include.cmd", "ViewerOne sends a hug", TemplateHelperFreshness.Memory),
        ComposingRows.Row(
            "include.builtin",
            "The stream has been live for 2h 5m.",
            TemplateHelperFreshness.Live,
            "builtin"
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
        return node.Key.ToLowerInvariant() switch
        {
            "include.cmd" when node.Arguments.Count == 1 && node.Arguments[0].Length > 0 =>
                await _commands.RenderAsync(scope, node.Arguments[0], cancellationToken),
            "include.builtin" when node.Arguments.Count == 1 && node.Arguments[0].Length > 0 =>
                await _builtins.RenderAsync(scope, node.Arguments[0], cancellationToken),
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
