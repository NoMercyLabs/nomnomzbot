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

namespace NomNomzBot.Domain.Identity.Events;

// Plane A/B authorization domain events (roles-permissions §2). Sealed classes deriving from the canonical
// DomainEventBase (string EventId, DateTimeOffset Timestamp, Guid BroadcasterId — inherited, never
// redeclared; the publisher sets the tenant BroadcasterId). The spec's "record" wording predates the
// codebase's class-based event base; these match the live convention (e.g. ChatClearedEvent).

/// <summary>A user's channel-management role was added, changed, or removed (schema B.1).</summary>
public sealed class ManagementRoleChangedEvent : DomainEventBase
{
    public required Guid TargetUserId { get; init; }
    public required ManagementRole? OldRole { get; init; }
    public required ManagementRole? NewRole { get; init; }
    public required MembershipSource Source { get; init; }
    public required Guid? ChangedByUserId { get; init; }
}

/// <summary>A viewer's community standing changed (schema B.2).</summary>
public sealed class CommunityStandingChangedEvent : DomainEventBase
{
    public required Guid TargetUserId { get; init; }
    public required CommunityStanding OldStanding { get; init; }
    public required CommunityStanding NewStanding { get; init; }
    public required StandingSource Source { get; init; }
}

/// <summary>An action's required level was overridden or reset for a channel (schema B.4).</summary>
public sealed class ActionLevelOverriddenEvent : DomainEventBase
{
    public required Guid ActionDefinitionId { get; init; }
    public required string ActionKey { get; init; }
    public required int? OldLevel { get; init; }
    public required int NewEffectiveLevel { get; init; }
    public required Guid SetByUserId { get; init; }
}

/// <summary>An individual <c>!permit</c> grant was created (schema B.5).</summary>
public sealed class PermitGrantedEvent : DomainEventBase
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required PermitGrantType GrantType { get; init; }
    public required ManagementRole? GrantedRole { get; init; }
    public required string? CapabilityActionKey { get; init; }
    public required Guid GrantedByUserId { get; init; }
    public required DateTime? ExpiresAt { get; init; }
}

/// <summary>A <c>!permit</c> grant was revoked or auto-expired (schema B.5).</summary>
public sealed class PermitRevokedEvent : DomainEventBase
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required Guid? RevokedByUserId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>A Gate-1 entry or Gate-2 per-action authorization was denied.</summary>
public sealed class AuthorizationDeniedEvent : DomainEventBase
{
    public required Guid CallerUserId { get; init; }
    public required string ActionKey { get; init; }
    public required int RequiredLevel { get; init; }
    public required int CallerLevel { get; init; }
    public required string Gate { get; init; }
}
