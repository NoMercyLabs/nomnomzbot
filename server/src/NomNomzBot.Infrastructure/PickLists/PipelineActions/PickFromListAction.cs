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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Services;

namespace NomNomzBot.Infrastructure.PickLists.PipelineActions;

/// <summary>
/// Pipeline action <c>pick_from_list</c> — draws one random entry from a named pick list and stores it in a
/// pipeline variable (default <c>pick</c>) for later steps to reference as <c>{{pick}}</c>. This is the
/// discoverable palette block the entity/service docs always promised but that never existed: previously the
/// ONLY way to use a pick list was to hand-type the magic <c>{list.pick.&lt;name&gt;}</c> string into some other
/// field. Storing into a variable also lets a SINGLE pick be reused across several steps (a bare
/// <c>{list.pick.name}</c> re-rolls on every occurrence). Fails with the service's reason when the list is
/// missing/empty, so the pipeline log is truthful.
/// </summary>
public sealed class PickFromListAction : ICommandAction
{
    private readonly IPickListService _lists;

    public PickFromListAction(IPickListService lists) => _lists = lists;

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

        ctx.Variables[variable] = picked.Value;
        return ActionResult.Success($"pick_from_list:{list}");
    }
}
