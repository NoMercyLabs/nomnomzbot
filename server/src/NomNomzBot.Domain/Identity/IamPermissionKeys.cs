// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// The closed Plane-C platform-IAM permission vocabulary (roles-permissions.md §C.1) — a DIFFERENT key
/// namespace from the Gate-2 <c>ActionDefinitions</c>. The ASP.NET <c>[Authorize(Policy = "&lt;key&gt;")]</c>
/// policy name IS the <c>IamPermission.Key</c> verbatim (platform-conventions.md §5 role-gate preamble), so
/// the dynamic policy provider discriminates Plane-C policies by membership in this set, and the seeder
/// seeds exactly this catalog. One definition — the provider and the seed can never drift.
/// </summary>
public static class IamPermissionKeys
{
    public const string TenantRead = "tenant:read";
    public const string TenantAccess = "tenant:access";
    public const string TenantSuspend = "tenant:suspend";
    public const string IamManage = "iam:manage";
    public const string IamPrincipalCreate = "iam:principal:create";
    public const string AuditRead = "audit:read";
    public const string FeatureFlagWrite = "featureflag:write";
    public const string BillingRead = "billing:read";
    public const string BillingRefund = "billing:refund";
    public const string PlatformAnalyticsRead = "platform:analytics:read";

    // Widget-gallery moderation (widgets-overlays.md §5c): review/pin community submissions —
    // approving code that renders on other people's overlays, so it is a sensitive platform grant.
    public const string GalleryReview = "gallery:review";

    // IPC dev-mode key registry (stream-admin.md §5.3): a tokenless local-socket hook-in — owner-only
    // (self-host short-circuits AuthorizePlatformAsync to allow; the routes 503 on SaaS regardless).
    public const string SystemIpcManage = "system:ipc:manage";

    // Full act-as impersonation of a registered user (stream-admin support): mints an access-only token
    // carrying the TARGET user's identity + roles so an operator can reproduce a support issue as that
    // user. Highly sensitive — the acting admin surfaces only in the non-authoritative `act` claim.
    public const string UserImpersonate = "user:impersonate";

    /// <summary>Every seeded Plane-C key (§C.1). The legacy alias <c>iam:audit:read</c> collapses to <c>audit:read</c>.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TenantRead,
        TenantAccess,
        TenantSuspend,
        IamManage,
        IamPrincipalCreate,
        AuditRead,
        FeatureFlagWrite,
        BillingRead,
        BillingRefund,
        PlatformAnalyticsRead,
        GalleryReview,
        SystemIpcManage,
        UserImpersonate,
    };
}
