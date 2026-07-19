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
}
