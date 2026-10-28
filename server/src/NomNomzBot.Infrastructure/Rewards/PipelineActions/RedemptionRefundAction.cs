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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Services;

namespace NomNomzBot.Infrastructure.Rewards.PipelineActions;

/// <summary>
/// Pipeline action that refunds the TRIGGERING channel-point redemption (CANCELED on Twitch — the viewer's
/// points come back). The legacy-parity failure path: a song reward whose track failed to queue, a TTS reward
/// with empty input. Routes through the SAME <see cref="IRewardService.SetRedemptionStatusAsync"/> path the
/// dashboard's refund button uses. Takes no parameters: the redemption id comes off the execution context
/// (seeded by <c>RewardRedeemedHandler</c> as both <c>ctx.RedemptionId</c> and the <c>{redemption.id}</c>
/// variable). Typed failures: the pipeline was not triggered by a redemption, or the redemption is no longer
/// pending (already fulfilled/refunded — the service refuses).
///
/// Usage example:
///   { "type": "redemption_refund" }
/// </summary>
public sealed class RedemptionRefundAction(IRewardService rewards) : ICommandAction
{
    public string ActionType => "redemption_refund";

    public LocalizedText Category => new("pipeline.category.rewards");

    public LocalizedText Description => new("pipeline.redemption_refund.description");

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? redemptionId = RedemptionContext.ResolveRedemptionId(ctx);
        if (redemptionId is null)
            return ActionResult.Failure(
                "redemption_refund requires a redemption-triggered pipeline (no redemption id in context)"
            );

        Result result = await rewards.SetRedemptionStatusAsync(
            ctx.BroadcasterId.ToString(),
            redemptionId,
            "CANCELED",
            RedemptionContext.ResolveRewardId(ctx),
            ctx.CancellationToken
        );
        return result.IsSuccess
            ? ActionResult.Success($"redemption {redemptionId} refunded")
            : ActionResult.Failure(
                result.ErrorMessage ?? $"redemption {redemptionId} could not be refunded"
            );
    }
}
