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
    /// <summary>The id of the user whose role changed.</summary>
    public required Guid TargetUserId { get; init; }

    /// <summary>The role the user had before. Empty when the user had no role.</summary>
    public required ManagementRole? OldRole { get; init; }

    /// <summary>The role the user has now. Empty when the role was removed.</summary>
    public required ManagementRole? NewRole { get; init; }

    /// <summary>How the role was given: TwitchBadge, HelixEditors, BotGrant or Owner.</summary>
    public required MembershipSource Source { get; init; }

    /// <summary>The id of the user who changed the role. Empty when the system changed it.</summary>
    public required Guid? ChangedByUserId { get; init; }
}

/// <summary>A viewer's community standing changed (schema B.2).</summary>
public sealed class CommunityStandingChangedEvent : DomainEventBase
{
    /// <summary>The id of the user whose standing changed.</summary>
    public required Guid TargetUserId { get; init; }

    /// <summary>The standing the user had before.</summary>
    public required CommunityStanding OldStanding { get; init; }

    /// <summary>The standing the user has now.</summary>
    public required CommunityStanding NewStanding { get; init; }

    /// <summary>Where the standing came from: ChatTags, EventSubBadge or HelixSeed.</summary>
    public required StandingSource Source { get; init; }
}

/// <summary>An action's required level was overridden or reset for a channel (schema B.4).</summary>
public sealed class ActionLevelOverriddenEvent : DomainEventBase
{
    /// <summary>The id of the action that changed.</summary>
    public required Guid ActionDefinitionId { get; init; }

    /// <summary>The key name of the action that changed.</summary>
    public required string ActionKey { get; init; }

    /// <summary>The level the channel had set before. Empty when the action used its default level.</summary>
    public required int? OldLevel { get; init; }

    /// <summary>The level that applies now.</summary>
    public required int NewEffectiveLevel { get; init; }

    /// <summary>The id of the user who made the change.</summary>
    public required Guid SetByUserId { get; init; }
}

/// <summary>An individual <c>!permit</c> grant was created (schema B.5).</summary>
public sealed class PermitGrantedEvent : DomainEventBase
{
    /// <summary>The id of the permit.</summary>
    public required Guid GrantId { get; init; }

    /// <summary>The id of the user who got the permit.</summary>
    public required Guid TargetUserId { get; init; }

    /// <summary>What kind of permit it is, a role or a single capability.</summary>
    public required PermitGrantType GrantType { get; init; }

    /// <summary>The role the permit gives. Empty when the permit gives a single capability.</summary>
    public required ManagementRole? GrantedRole { get; init; }

    /// <summary>The key name of the capability the permit gives. Empty when the permit gives a role.</summary>
    public required string? CapabilityActionKey { get; init; }

    /// <summary>The id of the user who gave the permit.</summary>
    public required Guid GrantedByUserId { get; init; }

    /// <summary>When the permit ends, in UTC time. Empty when it does not expire.</summary>
    public required DateTime? ExpiresAt { get; init; }
}

/// <summary>A <c>!permit</c> grant was revoked or auto-expired (schema B.5).</summary>
public sealed class PermitRevokedEvent : DomainEventBase
{
    /// <summary>The id of the permit.</summary>
    public required Guid GrantId { get; init; }

    /// <summary>The id of the user who lost the permit.</summary>
    public required Guid TargetUserId { get; init; }

    /// <summary>The id of the user who took the permit away. Empty when the system did it.</summary>
    public required Guid? RevokedByUserId { get; init; }

    /// <summary>Why the permit was taken away, in plain text.</summary>
    public required string Reason { get; init; }
}

/// <summary>A Gate-1 entry or Gate-2 per-action authorization was denied.</summary>
public sealed class AuthorizationDeniedEvent : DomainEventBase
{
    /// <summary>The id of the user who tried the action.</summary>
    public required Guid CallerUserId { get; init; }

    /// <summary>The key name of the action that was blocked.</summary>
    public required string ActionKey { get; init; }

    /// <summary>The permission level the action needs.</summary>
    public required int RequiredLevel { get; init; }

    /// <summary>The permission level the user has.</summary>
    public required int CallerLevel { get; init; }

    /// <summary>The name of the check that blocked the action. It is gate2, the per-action check.</summary>
    public required string Gate { get; init; }
}

/// <summary>A Plane-C platform-IAM access was evaluated (also persisted to IamAuditLog). SaaS-only.</summary>
public sealed class IamAccessEvaluatedEvent : DomainEventBase
{
    /// <summary>The id of the operator who asked for access.</summary>
    public required Guid PrincipalId { get; init; }

    /// <summary>The key name of the permission that was checked.</summary>
    public required string Permission { get; init; }

    /// <summary>The id of the channel the operator wants to reach. Empty when the check is not for one channel.</summary>
    public required Guid? TargetBroadcasterId { get; init; }

    /// <summary>True when the operator used emergency access.</summary>
    public required bool BreakGlass { get; init; }

    /// <summary>The result of the check: Allowed or Denied.</summary>
    public required IamOutcome Outcome { get; init; }
}
