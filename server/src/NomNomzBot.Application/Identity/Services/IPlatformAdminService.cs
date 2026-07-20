// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Plane-C privileged tenant operations (stream-admin.md §3.2) — the audited operator surface beside the
/// read-only <see cref="IAdminService"/>. Every method re-asserts its permission through
/// <c>IPlatformIamService.AuthorizePlatformAsync</c> (which audits the decision on SaaS; self-host with zero
/// principals is implicitly full). Feature-flag administration stays on its existing dedicated service
/// (<c>FeatureFlagAdminController</c>) — one owner per capability, no second door.
/// </summary>
public interface IPlatformAdminService
{
    /// <summary>Paged tenant listing with search/status/live filters. Requires <c>tenant:read</c>.</summary>
    Task<Result<PagedList<AdminTenantDto>>> ListTenantsAsync(
        Guid principalId,
        AdminTenantQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Tenant detail — status, tier, owner, membership count. Requires <c>tenant:read</c>.</summary>
    Task<Result<AdminTenantDetailDto>> GetTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Suspends a tenant (<c>suspended</c> | <c>platform_banned</c>): sets <c>Channels.Status</c> +
    /// <c>SuspendedAt</c>/<c>SuspendedReason</c> and emits <c>TenantSuspensionChangedEvent</c>. The bot
    /// lifecycle and tenant resolution both enforce the status. Requires <c>tenant:suspend</c>.
    /// </summary>
    Task<Result> SuspendTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        SuspendTenantRequest request,
        CancellationToken ct = default
    );

    /// <summary>Reinstates a suspended tenant to <c>active</c>, clearing the suspension fields. Requires <c>tenant:suspend</c>.</summary>
    Task<Result> ReinstateTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// Grants the acting principal audited support access to one tenant: a time-boxed
    /// <c>IamRoleAssignment</c> of the seeded <c>platform-support</c> role narrowed to that tenant.
    /// Justification is mandatory. Requires <c>tenant:access</c>.
    /// </summary>
    Task<Result<TenantAccessGrantDto>> BeginTenantAccessAsync(
        Guid principalId,
        Guid broadcasterId,
        BeginTenantAccessRequest request,
        CancellationToken ct = default
    );

    /// <summary>Ends a support-access grant (revokes the assignment). <c>NOT_FOUND</c> unless the grant is the caller's and still active.</summary>
    Task<Result> EndTenantAccessAsync(
        Guid principalId,
        Guid accessGrantId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Begins an act-as impersonation of a registered user: mints an ACCESS-ONLY JWT (no refresh) carrying the
    /// TARGET user's identity, tenant and roles — computed exactly as a normal login for the target, NEVER the
    /// operator's — with the acting operator recorded only in the non-authoritative <c>act</c>/<c>act_name</c>
    /// claims. Justification is mandatory and the target user id lands on the audit row. Requires
    /// <c>user:impersonate</c>.
    /// </summary>
    Task<Result<ImpersonationTokenDto>> StartImpersonationAsync(
        Guid actingPrincipalId,
        Guid targetUserId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>Paged Plane-C audit search by principal/tenant/permission/outcome/time. Requires <c>audit:read</c>.</summary>
    Task<Result<PagedList<IamAuditEntryDto>>> SearchAuditAsync(
        Guid principalId,
        AuditSearchQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
