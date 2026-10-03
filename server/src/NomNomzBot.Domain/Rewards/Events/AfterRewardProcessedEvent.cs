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

/// <summary>When the bot has finished handling a channel point reward redemption.</summary>
public sealed class AfterRewardProcessedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the reward.</summary>
    public required string RewardId { get; init; }

    /// <summary>The Twitch id of this redemption.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>True when the bot handled the redemption without an error.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>How long the bot took to handle the redemption.</summary>
    public required TimeSpan Duration { get; init; }
}
