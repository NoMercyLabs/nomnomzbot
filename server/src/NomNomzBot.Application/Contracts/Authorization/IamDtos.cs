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
