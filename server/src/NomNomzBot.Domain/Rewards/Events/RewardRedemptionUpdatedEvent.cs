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
/// Published when a custom channel-point reward redemption changes status
/// (<c>channel.channel_points_custom_reward_redemption.update</c>) — the streamer or a moderator marks a
/// queued redemption <c>fulfilled</c> or <c>canceled</c>. Distinct from <see cref="RewardRefundedEvent"/>,
/// which only models the refund case and carries neither the fulfilled status nor the viewer/title — this
/// event surfaces the full status transition and who it applied to.
/// </summary>
public sealed class RewardRedemptionUpdatedEvent : DomainEventBase
{
    public required string RedemptionId { get; init; }
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }

    /// <summary>The new redemption status: <c>fulfilled</c> or <c>canceled</c>.</summary>
    public required string Status { get; init; }
}
