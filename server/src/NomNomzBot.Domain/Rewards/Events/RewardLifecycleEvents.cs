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

/// <summary>Published when a custom channel point reward is created on Twitch.</summary>
public sealed class RewardCreatedEvent : DomainEventBase
{
    public required string TwitchRewardId { get; init; }
    public required string Title { get; init; }
    public required int Cost { get; init; }
    public required bool IsEnabled { get; init; }

    /// <summary>Twitch's <c>is_paused</c> flag (redemptions temporarily off while the reward stays listed).</summary>
    public bool IsPaused { get; init; }
}

/// <summary>Published when a custom channel point reward is updated on Twitch.</summary>
public sealed class RewardUpdatedEvent : DomainEventBase
{
    public required string TwitchRewardId { get; init; }
    public required string Title { get; init; }
    public required int Cost { get; init; }
    public required bool IsEnabled { get; init; }

    /// <summary>Twitch's <c>is_paused</c> flag — compared against the locally-synced state to derive the
    /// <c>reward.paused</c>/<c>reward.resumed</c> event-response transitions.</summary>
    public bool IsPaused { get; init; }
}

/// <summary>Published when a custom channel point reward is removed from Twitch.</summary>
public sealed class RewardRemovedEvent : DomainEventBase
{
    public required string TwitchRewardId { get; init; }
    public required string Title { get; init; }
}
