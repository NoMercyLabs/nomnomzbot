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
using NomNomzBot.Application.Rewards.Services;

namespace NomNomzBot.Infrastructure.Rewards.PipelineActions;

/// <summary>
/// Pipeline action that marks the TRIGGERING channel-point redemption FULFILLED on Twitch — the happy-path
/// closer for a reward pipeline that delivered what the viewer paid for. Routes through the SAME
/// <see cref="IRewardService.SetRedemptionStatusAsync"/> path the dashboard's fulfill button uses, so the Helix
/// status change and the local queue read model stay in lockstep. Takes no parameters: the redemption id comes
/// off the execution context (seeded by <c>RewardRedeemedHandler</c> as both <c>ctx.RedemptionId</c> and the
/// <c>{redemption.id}</c> variable). Typed failures: the pipeline was not triggered by a redemption, or the
/// redemption is no longer pending (already fulfilled/refunded — the service refuses).
///
/// Usage example:
///   { "type": "redemption_fulfill" }
/// </summary>
public sealed class RedemptionFulfillAction(IRewardService rewards) : ICommandAction
{
    public string ActionType => "redemption_fulfill";
    public string Category => "rewards";
    public string Description => "Mark the triggering redemption fulfilled on Twitch";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? redemptionId = RedemptionContext.ResolveRedemptionId(ctx);
        if (redemptionId is null)
            return ActionResult.Failure(
                "redemption_fulfill requires a redemption-triggered pipeline (no redemption id in context)"
            );

        Result result = await rewards.SetRedemptionStatusAsync(
            ctx.BroadcasterId.ToString(),
            redemptionId,
            "FULFILLED",
            ctx.CancellationToken
        );
        return result.IsSuccess
            ? ActionResult.Success($"redemption {redemptionId} fulfilled")
            : ActionResult.Failure(
                result.ErrorMessage ?? $"redemption {redemptionId} could not be fulfilled"
            );
    }
}
