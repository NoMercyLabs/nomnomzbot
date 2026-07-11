// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.PipelineActions;

/// <summary>
/// Pipeline action <c>set_counter</c> (commands-pipelines.md, G.4): upserts a per-channel named counter
/// to an absolute value. Params: <c>key</c> (slug), <c>value</c> (whole number). The value lands in
/// <c>{count.&lt;key&gt;}</c> for the rest of the run.
/// </summary>
public sealed class SetCounterAction(INamedCounterService counters) : ICommandAction
{
    public string ActionType => "set_counter";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? key = action.GetString("key");
        if (string.IsNullOrWhiteSpace(key))
            return ActionResult.Failure("set_counter requires a 'key'.");
        if (
            !long.TryParse(
                action.GetString("value"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long value
            )
        )
            return ActionResult.Failure("set_counter 'value' must be a whole number.");

        Result set = await counters.SetAsync(ctx.BroadcasterId, key, value, ctx.CancellationToken);
        if (set.IsFailure)
            return ActionResult.Failure(set.ErrorMessage ?? "set_counter failed.");

        string rendered = value.ToString(CultureInfo.InvariantCulture);
        ctx.Variables[$"count.{key.Trim().ToLowerInvariant()}"] = rendered;
        return ActionResult.Success(rendered);
    }
}

/// <summary>
/// Pipeline action <c>adjust_counter</c> (commands-pipelines.md, G.4): atomic increment of a per-channel
/// named counter (unset starts at the delta). Params: <c>key</c>, <c>delta</c> (default 1). The new
/// value lands in <c>{count.&lt;key&gt;}</c> for the rest of the run.
/// </summary>
public sealed class AdjustCounterAction(INamedCounterService counters) : ICommandAction
{
    public string ActionType => "adjust_counter";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? key = action.GetString("key");
        if (string.IsNullOrWhiteSpace(key))
            return ActionResult.Failure("adjust_counter requires a 'key'.");

        string? rawDelta = action.GetString("delta");
        long delta = 1;
        if (
            !string.IsNullOrWhiteSpace(rawDelta)
            && !long.TryParse(
                rawDelta,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out delta
            )
        )
            return ActionResult.Failure("adjust_counter 'delta' must be a whole number.");

        Result<long> adjusted = await counters.AdjustAsync(
            ctx.BroadcasterId,
            key,
            delta,
            ctx.CancellationToken
        );
        if (adjusted.IsFailure)
            return ActionResult.Failure(adjusted.ErrorMessage ?? "adjust_counter failed.");

        string rendered = adjusted.Value.ToString(CultureInfo.InvariantCulture);
        ctx.Variables[$"count.{key.Trim().ToLowerInvariant()}"] = rendered;
        return ActionResult.Success(rendered);
    }
}
