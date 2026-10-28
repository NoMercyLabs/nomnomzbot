// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// A platform-IAM principal (roles-permissions §4, Plane C). <see cref="ServiceAccountKey"/> is populated
/// once — only on service-account creation — and is never returned by reads (it exists only as a hash).
/// </summary>
public sealed record IamPrincipalDto(
    Guid Id,
    IamPrincipalType PrincipalType,
    Guid? UserId,
    string Name,
    bool IsActive,
    DateTime? ExpiresAt,
    string? ServiceAccountKey = null
);

/// <summary>A platform-IAM role assignment (roles-permissions §4, Plane C).</summary>
public sealed record IamRoleAssignmentDto(
    Guid Id,
    Guid PrincipalId,
    Guid RoleId,
    string RoleName,
    Guid? ScopeChannelId,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    string? Reason,
    DateTime CreatedAt
);

/// <summary>Request to provision a platform-IAM principal (roles-permissions §4, Plane C).</summary>
public sealed record CreatePrincipalRequest(
    IamPrincipalType PrincipalType,
    Guid? UserId,
    string DisplayName,
    IReadOnlyList<Guid> RoleIds,
    string? ServiceAccountName
);

/// <summary>A platform-IAM role with its permission bundle (roles-permissions §5.4) — the role picker's row.</summary>
public sealed record IamRoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> PermissionKeys
);

/// <summary>
/// One principal with its ACTIVE (non-revoked, non-expired) role assignments — the admin IAM screen's row
/// (roles-permissions §5.4).
/// </summary>
public sealed record IamPrincipalSummaryDto(
    Guid Id,
    IamPrincipalType PrincipalType,
    Guid? UserId,
    string Name,
    bool IsActive,
    DateTime? ExpiresAt,
    IReadOnlyList<IamRoleAssignmentDto> ActiveAssignments
);

/// <summary>Request to assign a platform-IAM role to a principal (roles-permissions §5.4).</summary>
public sealed record AssignIamRoleRequest(
    Guid PrincipalId,
    Guid RoleId,
    Guid? ScopeChannelId,
    DateTime? ExpiresAt,
    string? Reason
);
