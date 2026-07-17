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
/// Fired when an operator suspends / reinstates a tenant (<c>tenant:suspend</c>, stream-admin.md §2).
/// Platform-scoped (Plane-C operator action): the inherited <c>BroadcasterId</c> stays <c>Guid.Empty</c>;
/// the affected tenant rides in <see cref="TargetBroadcasterId"/>.
/// </summary>
public sealed class TenantSuspensionChangedEvent : DomainEventBase
{
    public required Guid PrincipalId { get; init; }
    public required Guid TargetBroadcasterId { get; init; }

    /// <summary><c>active</c> | <c>suspended</c> | <c>platform_banned</c>.</summary>
    public required string NewStatus { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Fired when an operator is granted audited support access to a tenant (<c>tenant:access</c>,
/// stream-admin.md §2) — a time-boxed, tenant-narrowed <c>IamRoleAssignment</c>.
/// </summary>
public sealed class TenantAccessGrantedEvent : DomainEventBase
{
    public required Guid PrincipalId { get; init; }
    public required Guid TargetBroadcasterId { get; init; }

    /// <summary>The created <c>IamRoleAssignment.Id</c> — revoking it ends the access.</summary>
    public required Guid AccessGrantId { get; init; }

    public required bool BreakGlass { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
