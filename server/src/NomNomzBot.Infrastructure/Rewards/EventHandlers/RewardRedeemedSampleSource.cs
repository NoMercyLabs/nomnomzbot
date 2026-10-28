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
            UserLogin = "sampleviewer",
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
