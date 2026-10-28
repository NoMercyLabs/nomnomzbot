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

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a custom channel-point reward redemption changes status
/// (<c>channel.channel_points_custom_reward_redemption.update</c>) — the streamer or a moderator marks a
/// queued redemption <c>fulfilled</c> or <c>canceled</c>. A refund is the <c>canceled</c> status; this
/// event surfaces the full status transition and who it applied to.
/// </summary>
public sealed class RewardRedemptionUpdatedEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this redemption update was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The Twitch id of this redemption.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>The Twitch id of the reward.</summary>
    public required string RewardId { get; init; }

    /// <summary>The reward title, as viewers see it.</summary>
    public required string RewardTitle { get; init; }

    /// <summary>The Twitch user id of the viewer who redeemed (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The new redemption status: <c>fulfilled</c> or <c>canceled</c>.</summary>
    public required string Status { get; init; }
}
