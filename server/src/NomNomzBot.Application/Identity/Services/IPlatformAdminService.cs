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
    /// claims. <paramref name="accessGrantId"/> must name an OPEN, time-boxed support-access grant
    /// (<see cref="BeginTenantAccessAsync"/>) belonging to the caller: minting is refused without one, and the
    /// token's expiry is clamped to the grant's remaining time, never longer. SaaS-only — refused on
    /// self-host. Justification is mandatory; the target user id AND the session both land on the audit row.
    /// Requires <c>user:impersonate</c> (owner-only — not bundled into platform-support).
    /// </summary>
    Task<Result<ImpersonationTokenDto>> StartImpersonationAsync(
        Guid actingPrincipalId,
        Guid targetUserId,
        Guid accessGrantId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ends an impersonation session: revokes the backing support-access grant AND the minted token's
    /// <c>sid</c> through <see cref="Abstractions.Auth.ISessionRevocationService"/>, so the same token fails
    /// authentication on its very next request. <c>NOT_FOUND</c> unless the grant is the caller's and still
    /// active. SaaS-only. Requires <c>user:impersonate</c>.
    /// </summary>
    Task<Result> EndImpersonationAsync(
        Guid actingPrincipalId,
        Guid accessGrantId,
        CancellationToken ct = default
    );

    /// <summary>Paged Plane-C audit search by principal/tenant/permission/outcome/time. Requires <c>audit:read</c>.</summary>
    Task<Result<PagedList<IamAuditEntryDto>>> SearchAuditAsync(
        Guid principalId,
        AuditSearchQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Every LIVE per-tenant quota override for <paramref name="broadcasterId"/>. Requires <c>tenant:read</c>
    /// (read-only; setting/clearing one is the separate, more sensitive <c>tenant:quota:manage</c>).
    /// </summary>
    Task<Result<IReadOnlyList<TenantLimitOverrideDto>>> ListTenantLimitOverridesAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sets (or replaces) the one LIVE override for <c>(broadcasterId, request.LimitKey)</c> — the exact
    /// ceiling <c>ResourceQuotaService.CheckAsync</c> enforces for every subsequent write from this tenant,
    /// ahead of both the NEAR_FREE safety baseline and the tier-resolved COST_DRIVING limit. Requires
    /// <c>tenant:quota:manage</c>; a reason is mandatory and audited.
    /// </summary>
    Task<Result<TenantLimitOverrideDto>> SetTenantLimitOverrideAsync(
        Guid principalId,
        Guid broadcasterId,
        SetTenantLimitOverrideRequest request,
        CancellationToken ct = default
    );

    /// <summary>Clears the LIVE override for <c>(broadcasterId, limitKey)</c>, reverting to the normal resolution. Requires <c>tenant:quota:manage</c>.</summary>
    Task<Result> ClearTenantLimitOverrideAsync(
        Guid principalId,
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct = default
    );

    /// <summary>
    /// Forces a re-application of any pending EF Core migrations — the same migrator the API runs at
    /// startup, invoked on demand for an operator recovering a deploy whose auto-migration step was skipped
    /// or interrupted. Database-wide (one shared schema across every tenant); audited against the tenant the
    /// operator was working on when they reached for it. Requires <c>tenant:remigrate</c>.
    /// </summary>
    Task<Result<TenantRemigrationResultDto>> ForceRemigrationAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// The counted blast radius of erasing <paramref name="broadcasterId"/> whole — every one of the 116
    /// tenant-scoped tables <c>ChannelBlastRadiusSources</c> curates, real row counts, grouped exactly as the
    /// owner's own self-service delete-preview renders them. Requires <c>tenant:erase</c>; mutates nothing.
    /// </summary>
    Task<Result<ChannelDeletePreviewDto>> PreviewEraseTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Erases a tenant whole: a SOFT delete (house default) of the channel — the same <c>ChannelService</c>
    /// path the owner's own self-service delete uses, with its 30-day restore window — chosen because it is
    /// the existing, tested tenant-lifecycle mechanism, and because an operator-initiated offboarding is not
    /// itself a GDPR Article-17 request (a genuine subject erasure already exists, irreversible, on
    /// <c>ComplianceController</c>). Requires <c>tenant:erase</c>; justification is mandatory and audited.
    /// </summary>
    Task<Result> EraseTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// A machine-readable JSON export of every row <c>ChannelBlastRadiusSources</c> attributes to this tenant
    /// — the operator-initiated sibling of the owner's own data, covering the CHANNEL's data rather than one
    /// subject's (contrast <c>ComplianceController.ExportSubjectData</c>). Read-only. Requires
    /// <c>tenant:erase</c> (the same key that gates seeing what erasure would destroy).
    /// </summary>
    Task<Result<string>> ExportTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
