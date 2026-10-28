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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a channel point reward is redeemed by a viewer.
/// </summary>
public sealed class RewardRedeemedEvent : DomainEventBase
{
    /// <summary>The Twitch id of the reward.</summary>
    public required string RewardId { get; init; }

    /// <summary>The reward title, as viewers see it.</summary>
    public required string RewardTitle { get; init; }

    /// <summary>The Twitch id of this redemption.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>The Twitch user id of the viewer who redeemed (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The viewer's login (lowercase, no spaces), or null when the source gave none.</summary>
    public string? UserLogin { get; init; }

    /// <summary>The channel points the viewer paid.</summary>
    public required int Cost { get; init; }

    /// <summary>The text the viewer typed when redeeming. Empty when the reward asks for no text.</summary>
    public string? UserInput { get; init; }
}
