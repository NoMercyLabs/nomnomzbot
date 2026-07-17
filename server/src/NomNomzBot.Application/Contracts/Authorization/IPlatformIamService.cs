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

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Plane-C platform IAM (roles-permissions §3.7) — access control for NoMercy Labs operators over the hosted
/// platform. SaaS-only: on self-host no <c>IamPrincipal</c>s exist, so the single operator is implicitly full
/// (every authorize returns true and writes no audit). On SaaS, every authorize is audited.
/// </summary>
public interface IPlatformIamService
{
    /// <summary>
    /// Does the principal hold <paramref name="permissionKey"/> (optionally scoped to a tenant)? Self-host
    /// (no principals) → true, no audit. SaaS → checks effective permissions, writes <c>IamAuditLog</c>
    /// (Allowed|Denied), and emits <c>IamAccessEvaluatedEvent</c>.
    /// </summary>
    Task<Result<bool>> AuthorizePlatformAsync(
        Guid principalId,
        string permissionKey,
        Guid? targetBroadcasterId,
        bool breakGlass,
        string? justification,
        CancellationToken cancellationToken = default
    );

    /// <summary>The IAM principal for an authenticated platform user, or null if the user is not a principal.</summary>
    Task<Result<IamPrincipalDto?>> ResolvePrincipalAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// True when ANY <c>IamPrincipal</c> exists — the deployment-shape fact the whole plane keys on
    /// (self-host = zero principals → the operator is implicitly full; SaaS = principals exist → default-deny).
    /// The authorization handler consults this to decide the no-principal-row case for a platform-marked
    /// caller: implicit-full on self-host, fail-closed misconfiguration on SaaS.
    /// </summary>
    Task<bool> HasAnyPrincipalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions an employee (over an existing user) or a service account (generates a key, stores its hash,
    /// returns the key once on the principal) and assigns the requested roles. Requires the acting principal
    /// to hold <c>iam:principal:create</c>.
    /// </summary>
    Task<Result<IamPrincipalDto>> CreatePrincipalAsync(
        Guid actingPrincipalId,
        CreatePrincipalRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Assigns a role to a principal, optionally tenant-scoped + time-boxed. Requires caller <c>iam:manage</c>.</summary>
    Task<Result<IamRoleAssignmentDto>> AssignRoleAsync(
        Guid actingPrincipalId,
        Guid principalId,
        Guid roleId,
        Guid? scopeChannelId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>Revokes an assignment (sets <c>RevokedAt</c>). Requires caller <c>iam:manage</c>.</summary>
    Task<Result> RevokeAssignmentAsync(
        Guid actingPrincipalId,
        Guid assignmentId,
        string? reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>Effective permission keys for a principal — the union over active, non-expired, in-scope assignments.</summary>
    Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(
        Guid principalId,
        Guid? scopeChannelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The role catalog with each role's permission bundle — the admin screen's role picker.</summary>
    Task<Result<IReadOnlyList<IamRoleDto>>> ListRolesAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Every principal with its ACTIVE assignments — the admin IAM screen's principal list.</summary>
    Task<Result<IReadOnlyList<IamPrincipalSummaryDto>>> ListPrincipalsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deactivates a principal AND clears the backing employee user's <c>IsPlatformPrincipal</c> marker (the
    /// demote). A principal cannot deactivate itself — the lockout guard. Requires caller <c>iam:manage</c>.
    /// </summary>
    Task<Result> DeactivatePrincipalAsync(
        Guid actingPrincipalId,
        Guid principalId,
        string? reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reactivates a principal and restores the employee user's <c>IsPlatformPrincipal</c> marker. Requires caller <c>iam:manage</c>.</summary>
    Task<Result> ReactivatePrincipalAsync(
        Guid actingPrincipalId,
        Guid principalId,
        CancellationToken cancellationToken = default
    );
}
