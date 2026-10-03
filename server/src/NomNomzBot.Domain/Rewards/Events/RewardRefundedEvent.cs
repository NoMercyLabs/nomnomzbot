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

/// <summary>When a channel point reward redemption is refunded to the viewer.</summary>
public sealed class RewardRefundedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the reward.</summary>
    public required string RewardId { get; init; }

    /// <summary>The Twitch id of the redemption that was refunded.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>Why the redemption was refunded.</summary>
    public required string Reason { get; init; }
}
