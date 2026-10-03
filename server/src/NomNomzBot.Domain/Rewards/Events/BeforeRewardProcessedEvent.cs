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

/// <summary>When the bot is about to handle a channel point reward redemption.</summary>
public sealed class BeforeRewardProcessedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the reward.</summary>
    public required string RewardId { get; init; }

    /// <summary>The Twitch id of this redemption.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>The Twitch user id of the viewer who redeemed (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The text the viewer typed when redeeming. Empty when the reward asks for no text.</summary>
    public required string UserInput { get; init; }
}
