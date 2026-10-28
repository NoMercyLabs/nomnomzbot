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

using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Rewards.PipelineActions;

/// <summary>
/// Shared seam for the redemption status actions: the triggering redemption id off the execution context.
/// <c>RewardRedeemedHandler</c> seeds it twice — <see cref="PipelineExecutionContext.RedemptionId"/> for the
/// reward-bound pipeline path, and the <c>{redemption.id}</c> variable which also survives the generic
/// event-response path — so both are honored, context property first.
/// </summary>
internal static class RedemptionContext
{
    public static string? ResolveRedemptionId(PipelineExecutionContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.RedemptionId))
            return ctx.RedemptionId;
        return
            ctx.Variables.TryGetValue("redemption.id", out string? seeded)
            && !string.IsNullOrWhiteSpace(seeded)
            ? seeded
            : null;
    }

    /// <summary>The triggering redemption's reward id (context property first, then the seeded variable).</summary>
    public static string? ResolveRewardId(PipelineExecutionContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.RewardId))
            return ctx.RewardId;
        return
            ctx.Variables.TryGetValue("reward.id", out string? seeded)
            && !string.IsNullOrWhiteSpace(seeded)
            ? seeded
            : null;
    }
}
