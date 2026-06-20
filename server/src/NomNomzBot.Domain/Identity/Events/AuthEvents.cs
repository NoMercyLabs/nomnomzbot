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

public sealed class UserRegisteredEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required string TwitchUserId { get; init; }
    public required string Username { get; init; }
    public required string Platform { get; init; }
}

public sealed class UserLoggedInEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required Guid SessionId { get; init; }
    public required string ClientType { get; init; }
}

public sealed class UserLoggedOutEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required Guid SessionId { get; init; }
    public required string Reason { get; init; }
}

public sealed class ChannelOnboardedEvent : DomainEventBase
{
    public required Guid OwnerUserId { get; init; }
    public required string TwitchChannelId { get; init; }
    public required string Name { get; init; }
}

public sealed class ChannelSuspendedEvent : DomainEventBase
{
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public Guid? ActorUserId { get; init; }
}

public sealed class ChannelReinstatedEvent : DomainEventBase
{
    public Guid? ActorUserId { get; init; }
}

public sealed class BotAccountAuthorizedEvent : DomainEventBase
{
    public required Guid BotAccountId { get; init; }
    public required string IdentityType { get; init; }
    public required string BotUsername { get; init; }
}

public sealed class BotAccountDisconnectedEvent : DomainEventBase
{
    public required Guid BotAccountId { get; init; }
    public required string Reason { get; init; }
}

public sealed class RefreshTokenReuseDetectedEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required Guid SessionId { get; init; }
    public required string TokenHash { get; init; }
}
