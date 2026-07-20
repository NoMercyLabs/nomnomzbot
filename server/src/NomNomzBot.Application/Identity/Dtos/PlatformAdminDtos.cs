// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>One tenant row for the operator console (stream-admin.md §4 Platform admin).</summary>
public sealed record AdminTenantDto(
    Guid Id,
    string Name,
    string TwitchChannelId,
    string Status,
    string BillingTierKey,
    bool IsLive,
    DateTime CreatedAt,
    DateTime? SuspendedAt
);

/// <summary>Tenant detail for the operator console — status, tier, owner, counts (stream-admin.md §4).</summary>
public sealed record AdminTenantDetailDto(
    Guid Id,
    string Name,
    string TwitchChannelId,
    string Status,
    string? SuspendedReason,
    string BillingTierKey,
    string DeploymentMode,
    Guid OwnerUserId,
    string OwnerDisplayName,
    int MembershipCount,
    DateTime CreatedAt,
    DateTime? SuspendedAt
);

/// <summary>Tenant-list filters (stream-admin.md §4).</summary>
public sealed record AdminTenantQuery(string? Search, string? Status, bool? IsLive);

/// <summary><c>NewStatus</c> is <c>suspended</c> | <c>platform_banned</c> (stream-admin.md §4).</summary>
public sealed record SuspendTenantRequest(string NewStatus, string Reason);

/// <summary>Request for audited support access to one tenant (stream-admin.md §4).</summary>
public sealed record BeginTenantAccessRequest(
    string Justification,
    bool BreakGlass,
    DateTime? ExpiresAt
);

/// <summary>An audited support-access grant — a time-boxed, tenant-narrowed role assignment (stream-admin.md §4).</summary>
public sealed record TenantAccessGrantDto(
    Guid Id,
    Guid PrincipalId,
    Guid TargetBroadcasterId,
    string Justification,
    bool BreakGlass,
    DateTime GrantedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt
);

/// <summary>Request to begin an act-as impersonation of a registered user — justification is mandatory (stream-admin.md §4).</summary>
public sealed record ImpersonateUserRequest(string Justification);

/// <summary>
/// The minted act-as token for an impersonation session: an access-only JWT carrying the TARGET user's
/// identity + roles (never the operator's), its expiry, and the impersonated user's profile (stream-admin.md §4).
/// </summary>
public sealed record ImpersonationTokenDto(string AccessToken, DateTime ExpiresAt, UserDto User);

/// <summary>Plane-C audit-log search filters (stream-admin.md §4).</summary>
public sealed record AuditSearchQuery(
    Guid? PrincipalId,
    Guid? TargetBroadcasterId,
    string? Permission,
    string? Outcome,
    DateTime? From,
    DateTime? To
);

/// <summary>One Plane-C audit row for the operator console (stream-admin.md §4).</summary>
public sealed record IamAuditEntryDto(
    long Id,
    Guid PrincipalId,
    string PrincipalType,
    string Permission,
    Guid? TargetBroadcasterId,
    string? TargetResource,
    string? Justification,
    bool BreakGlass,
    string Outcome,
    DateTime OccurredAt
);
