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

namespace NomNomzBot.Domain.Identity.Events;

// Identity-auth domain events (identity-auth §2). Each inherits EventId / Timestamp / BroadcasterId from
// DomainEventBase (set by the publisher) and adds only its own payload. Tenant-scoped events carry the
// owning channel id in BroadcasterId; platform-scoped events leave it at Guid.Empty (the platform sentinel).

/// <summary>When a new user account is created.</summary>
public sealed class UserRegisteredEvent : DomainEventBase
{
    /// <summary>The id of the new user account.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the new user.</summary>
    public required string TwitchUserId { get; init; }

    /// <summary>The name of the new user.</summary>
    public required string Username { get; init; }

    /// <summary>The platform the user signed up with. It is twitch.</summary>
    public required string Platform { get; init; }
}

/// <summary>When a user logs in.</summary>
public sealed class UserLoggedInEvent : DomainEventBase
{
    /// <summary>The id of the user who logged in.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The id of the new login session.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>The kind of app the user logged in with, as the app reports it. It is web when the app does not say.</summary>
    public required string ClientType { get; init; }
}

/// <summary>When a user logs out or a login session ends.</summary>
public sealed class UserLoggedOutEvent : DomainEventBase
{
    /// <summary>The id of the user who logged out.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The id of the login session that ended.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>Why the session ended, in plain text.</summary>
    public required string Reason { get; init; }
}

/// <summary>When a streamer finishes setting up a channel on the bot.</summary>
public sealed class ChannelOnboardedEvent : DomainEventBase
{
    /// <summary>The id of the user who owns the channel.</summary>
    public required Guid OwnerUserId { get; init; }

    /// <summary>The Twitch user id (a number as text) of the channel.</summary>
    public required string TwitchChannelId { get; init; }

    /// <summary>The name of the channel.</summary>
    public required string Name { get; init; }
}

/// <summary>When a channel is suspended on the platform.</summary>
public sealed class ChannelSuspendedEvent : DomainEventBase
{
    /// <summary>The new status of the channel. The channel status values are active, suspended, churned and platform_banned.</summary>
    public required string Status { get; init; }

    /// <summary>Why the channel was suspended. Empty when no reason was given.</summary>
    public string? Reason { get; init; }

    /// <summary>The id of the user who suspended the channel. Empty when the system did it.</summary>
    public Guid? ActorUserId { get; init; }
}

/// <summary>When a suspended channel is allowed to use the bot again.</summary>
public sealed class ChannelReinstatedEvent : DomainEventBase
{
    /// <summary>The id of the user who reinstated the channel. Empty when the system did it.</summary>
    public Guid? ActorUserId { get; init; }
}

/// <summary>When a bot account is connected and authorized.</summary>
public sealed class BotAccountAuthorizedEvent : DomainEventBase
{
    /// <summary>The id of the bot account.</summary>
    public required Guid BotAccountId { get; init; }

    /// <summary>The kind of identity the bot account uses: shared or custom.</summary>
    public required string IdentityType { get; init; }

    /// <summary>The login name of the bot account, in lowercase.</summary>
    public required string BotUsername { get; init; }
}

/// <summary>When a bot account is disconnected.</summary>
public sealed class BotAccountDisconnectedEvent : DomainEventBase
{
    /// <summary>The id of the bot account.</summary>
    public required Guid BotAccountId { get; init; }

    /// <summary>Why the bot account was disconnected, in plain text.</summary>
    public required string Reason { get; init; }
}

/// <summary>When a login token is used a second time, which can mean it was stolen.</summary>
public sealed class RefreshTokenReuseDetectedEvent : DomainEventBase
{
    /// <summary>The id of the user the token belongs to.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The id of the login session the token belongs to.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>A hash that identifies the reused token. It is not the token itself.</summary>
    public required string TokenHash { get; init; }
}
