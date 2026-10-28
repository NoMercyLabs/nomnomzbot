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

namespace NomNomzBot.Domain.Community.Events;

/// <summary>
/// Published when a new user follows the channel — Twitch (EventSub channel.follow) or Kick (webhook
/// channel.followed); <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class FollowEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this follow was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The Twitch user id of the viewer (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The display name of the viewer, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The login name of the viewer (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>When the viewer followed, in UTC time.</summary>
    public required DateTimeOffset FollowedAt { get; init; }
}
