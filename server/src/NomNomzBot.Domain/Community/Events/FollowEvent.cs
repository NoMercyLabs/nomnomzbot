// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
