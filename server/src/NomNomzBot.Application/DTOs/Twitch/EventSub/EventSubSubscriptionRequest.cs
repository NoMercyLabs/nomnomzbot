// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
