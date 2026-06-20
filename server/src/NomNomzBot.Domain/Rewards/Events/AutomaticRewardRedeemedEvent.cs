// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a viewer redeems one of Twitch's built-in automatic channel-point rewards
/// (<c>channel.channel_points_automatic_reward_redemption.add</c> v2) — e.g.
/// <c>send_highlighted_message</c>, <c>random_sub_emote_unlock</c>, <c>celebration</c>. Unlike a custom
/// reward, the streamer does not define these; Twitch fixes the reward kind via <see cref="RewardType"/>
/// and the channel-point price via <see cref="Cost"/>.
/// </summary>
public sealed class AutomaticRewardRedeemedEvent : DomainEventBase
{
    public required string RedemptionId { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserDisplayName { get; init; }

    /// <summary>The fixed reward kind, e.g. <c>send_highlighted_message</c> or <c>celebration</c>.</summary>
    public required string RewardType { get; init; }

    /// <summary>The channel-point price paid for the redemption.</summary>
    public required int Cost { get; init; }

    /// <summary>The unlocked emote's id when the reward unlocks an emote; otherwise <c>null</c>.</summary>
    public string? UnlockedEmoteId { get; init; }

    /// <summary>The accompanying message text when the reward carries one; otherwise <c>null</c>.</summary>
    public string? Message { get; init; }
}
