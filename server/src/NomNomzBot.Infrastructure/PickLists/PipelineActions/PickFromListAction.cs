// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Services;

namespace NomNomzBot.Infrastructure.PickLists.PipelineActions;

/// <summary>
/// Pipeline action <c>pick_from_list</c> — draws one random entry from a named pick list, resolves the entry
/// through <see cref="ITemplateResolver"/>, and stores the RESOLVED text in a pipeline variable (default
/// <c>pick</c>) for later steps to reference as <c>{{pick}}</c>. Resolving before storing matters twice over:
/// the engine's substitution is single-pass, so a stored entry like <c>"{user} is {list.pick.adjectives}"</c>
/// would otherwise stay literal when a later step substitutes <c>{pick}</c>; and it makes the block's promise
/// real — one pick is rolled ONCE, fully resolved (nested <c>{list.pick.*}</c>, <c>{user}</c>, grammar vars),
/// and reused verbatim across every later step (a bare <c>{list.pick.name}</c> re-rolls on every occurrence).
/// Fails with the service's reason when the list is missing/empty, so the pipeline log is truthful.
/// </summary>
public sealed class PickFromListAction : ICommandAction
{
    private readonly IPickListService _lists;
    private readonly ITemplateResolver _resolver;

    public PickFromListAction(IPickListService lists, ITemplateResolver resolver)
    {
        _lists = lists;
        _resolver = resolver;
    }

    public string ActionType => "pick_from_list";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? list = action.GetString("list") ?? action.GetString("name");
        if (string.IsNullOrWhiteSpace(list))
            return ActionResult.Failure(
                "pick_from_list requires a 'list' parameter (a pick-list name)."
            );

        string variable =
            action.GetString("variable") is string v && !string.IsNullOrWhiteSpace(v)
                ? v.Trim()
                : "pick";

        Result<string> picked = await _lists.PickRandomAsync(
            ctx.BroadcasterId,
            list.Trim(),
            ctx.CancellationToken
        );
        if (picked.IsFailure)
            return ActionResult.Failure(
                picked.ErrorMessage ?? $"pick_from_list couldn't pick from '{list}'."
            );

        // Resolve the picked entry NOW, against the pipeline's current variables — the roll happens once,
        // and every later read of the variable sees the same fully-resolved string.
        string resolved = await _resolver.ResolveAsync(
            picked.Value,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        ctx.Variables[variable] = resolved;
        return ActionResult.Success($"pick_from_list:{list}");
    }
}
