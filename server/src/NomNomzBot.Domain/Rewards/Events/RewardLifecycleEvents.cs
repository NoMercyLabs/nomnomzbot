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
}

/// <summary>Published when a custom channel point reward is updated on Twitch.</summary>
public sealed class RewardUpdatedEvent : DomainEventBase
{
    public required string TwitchRewardId { get; init; }
    public required string Title { get; init; }
    public required int Cost { get; init; }
    public required bool IsEnabled { get; init; }
}

/// <summary>Published when a custom channel point reward is removed from Twitch.</summary>
public sealed class RewardRemovedEvent : DomainEventBase
{
    public required string TwitchRewardId { get; init; }
    public required string Title { get; init; }
}
