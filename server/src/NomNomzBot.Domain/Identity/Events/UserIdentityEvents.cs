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

/// <summary>
/// Published when a proven external identity is bound to a user (platform-identity §2). User-scoped: the
/// publisher leaves <c>BroadcasterId</c> at the platform sentinel (<see cref="Guid.Empty"/>).
/// </summary>
public sealed class UserIdentityLinkedEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderUserId { get; init; }
    public required string ProviderUsername { get; init; }
}

/// <summary>Published when an external identity is removed from a user (platform-identity §2).</summary>
public sealed class UserIdentityUnlinkedEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderUserId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Published when the primary flag moves to a different provider for a user (platform-identity §2).</summary>
public sealed class PrimaryIdentityChangedEvent : DomainEventBase
{
    public required Guid UserId { get; init; }
    public required string Provider { get; init; }
}

/// <summary>
/// Published when a bare-viewer <see cref="Domain.Identity.Entities.User"/> row is absorbed into a real
/// account during a link (platform-identity §3.1a). Every per-viewer domain re-keys
/// <see cref="AbsorbedUserId"/> → <see cref="IntoUserId"/>.
/// </summary>
public sealed class ViewerRowAbsorbedEvent : DomainEventBase
{
    public required Guid AbsorbedUserId { get; init; }
    public required Guid IntoUserId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderUserId { get; init; }
}
