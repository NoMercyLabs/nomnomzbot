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

namespace NomNomzBot.Domain.Stream.Events;

// Guest Star domain events (EventSub channel.guest_star_* — BETA on Twitch's side; payload shapes may move).
// Each inherits EventId / Timestamp / BroadcasterId from DomainEventBase (publisher sets the resolved tenant).

/// <summary>Published when a Guest Star session begins (<c>channel.guest_star_session.begin</c>).</summary>
public sealed class GuestStarSessionBeganEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>Published when a Guest Star session ends (<c>channel.guest_star_session.end</c>).</summary>
public sealed class GuestStarSessionEndedEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
}

/// <summary>
/// Published when a guest's slot state changes (<c>channel.guest_star_guest.update</c>). The guest and moderator
/// fields are situational (a slot vacated by a guest carries no guest identity); <see cref="State"/> is one of
/// <c>invited</c>, <c>ready</c>, <c>backstage</c>, <c>live</c>, <c>removed</c>, <c>accepted</c>.
/// </summary>
public sealed class GuestStarGuestUpdatedEvent : DomainEventBase
{
    public required string SessionId { get; init; }
    public string? ModeratorId { get; init; }
    public string? GuestUserId { get; init; }
    public string? GuestDisplayName { get; init; }
    public string? GuestLogin { get; init; }

    /// <summary>The guest's slot state: invited / ready / backstage / live / removed / accepted.</summary>
    public required string State { get; init; }
    public string? SlotId { get; init; }
}

/// <summary>
/// Published when the channel's Guest Star settings change (<c>channel.guest_star_settings.update</c>).
/// <see cref="GroupLayout"/> is one of <c>tiled</c>, <c>screenshare</c>, <c>horizontal_top</c>, etc.
/// </summary>
public sealed class GuestStarSettingsUpdatedEvent : DomainEventBase
{
    public required bool IsModeratorSendLiveEnabled { get; init; }
    public required int SlotCount { get; init; }
    public required bool IsBrowserSourceAudioEnabled { get; init; }

    /// <summary>The browser-source group layout: tiled / screenshare / horizontal_top / ….</summary>
    public required string GroupLayout { get; init; }
}
