// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A durable, per-tenant security notice (S-IMPERSONATION-NOTICE) — the after-the-fact record of a
/// security- or trust-affecting platform action against a channel, surviving the owner being offline for
/// the entire window the transient <c>DashboardHub</c> alert covers. The live SignalR alert
/// (<see cref="Api.Hubs.IDashboardNotifier.SendAlertAsync"/> — Api layer) stays the real-time path; this
/// row is the recoverable one, listable and acknowledgeable from the dashboard whenever the owner next
/// signs in. <see cref="AcknowledgedAt"/> null means unread.
/// </summary>
public class SecurityNotice : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The affected channel — the tenant that must see this notice.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Machine-readable kind, e.g. <c>impersonation_started</c>, <c>tenant_access_granted</c>.</summary>
    [MaxLength(64)]
    public string NoticeType { get; set; } = null!;

    /// <summary>Human-readable one-liner shown in the notice list.</summary>
    [MaxLength(500)]
    public string Summary { get; set; } = null!;

    /// <summary>The IAM principal (platform operator) who acted. Null when the actor could not be resolved.</summary>
    public Guid? ActorPrincipalId { get; set; }

    /// <summary>The impersonated/affected user, when the action targeted one (impersonation only).</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>The backing access grant (<c>IamRoleAssignment.Id</c>), when the action rode on one.</summary>
    public Guid? AccessGrantId { get; set; }

    /// <summary>The support session's stated justification.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>What the grant/session covered — a scope label (e.g. break-glass channel-wide access).</summary>
    [MaxLength(200)]
    public string? Scope { get; set; }

    /// <summary>When the underlying grant/session expires (or expired), when known.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Null = unread. Set once the owner acknowledges the notice; sticks across reloads.</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>The user who acknowledged the notice.</summary>
    public Guid? AcknowledgedByUserId { get; set; }
}
