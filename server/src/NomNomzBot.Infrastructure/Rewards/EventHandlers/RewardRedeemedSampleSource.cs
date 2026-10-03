// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// A channel-point redemption as a test-run trigger, with the variables <see cref="RewardRedeemedHandler"/> hands a
/// reward's pipeline. It runs the generic redemption event response, the same key the handler falls back to.
/// </summary>
public sealed class RewardRedeemedSampleSource : ITriggerSampleSource
{
    public const string ResponseKey = "channel.channel_points_custom_reward_redemption.add";

    public TriggerSample Sample(DateTimeOffset now)
    {
        RewardRedeemedEvent redemption = new()
        {
            BroadcasterId = Guid.Empty,
            RewardId = "sample-reward-id",
            RewardTitle = "Hydrate!",
            RedemptionId = "sample-redemption-id",
            UserId = "100000042",
            UserDisplayName = "SampleViewer",
            Cost = 500,
            UserInput = "Drink some water",
            OccurredAt = now,
        };

        return new(
            "reward.redeemed",
            ResponseKey,
            redemption.UserId,
            redemption.UserDisplayName,
            RewardRedeemedHandler.BuildVariables(redemption)
        );
    }
}
