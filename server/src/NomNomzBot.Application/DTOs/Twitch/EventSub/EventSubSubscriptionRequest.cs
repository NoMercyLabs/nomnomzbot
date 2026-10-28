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

using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Application.DTOs.Twitch.EventSub;

/// <summary>
/// The create-one-subscription request the service hands to the transport, which turns it into the Twitch
/// <c>POST /eventsub/subscriptions</c> body (twitch-eventsub §4.2).
/// </summary>
public sealed record EventSubSubscriptionRequest
{
    public required Guid BroadcasterId { get; init; }
    public required string TwitchBroadcasterUserId { get; init; }
    public required string EventType { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, string> Condition { get; init; }

    /// <summary>Which Twitch token authorizes the create (Broadcaster | Bot | Moderator).</summary>
    public EventSubTokenOwnerKind? UserAccessTokenOwner { get; init; }
}
