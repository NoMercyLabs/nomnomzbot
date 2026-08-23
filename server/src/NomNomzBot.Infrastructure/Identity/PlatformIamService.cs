// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Plane-C platform IAM (roles-permissions §3.7). On self-host every authorize short-circuits to true with no
/// audit (the operator is implicitly full) — decided from the DEPLOYMENT MODE (fix D2), never from whether any
/// <c>IamPrincipal</c> row exists: the self-host owner is bootstrapped with a real principal + role assignment
/// (<c>AuthService.MintPlatformOwnerPrincipalAsync</c>), so principal existence alone can no longer distinguish
/// self-host from SaaS. On SaaS, a principal's effective permissions are the union over its active, non-expired,
/// in-scope role assignments, every authorize is written to the append-only audit log, and management ops are
/// themselves gated on iam:* keys.
/// </summary>
public sealed class PlatformIamService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock,
    DeploymentContext deploymentContext
) : IPlatformIamService
{
    private const string ManagePermission = "iam:manage";
    private const string CreatePrincipalPermission = "iam:principal:create";

    public async Task<Result<bool>> AuthorizePlatformAsync(
        Guid principalId,
        string permissionKey,
        Guid? targetBroadcasterId,
        bool breakGlass,
        string? justification,
        CancellationToken cancellationToken = default,
        string? targetResource = null
    )
    {
        if (!IsSaas)
            return Result.Success(true); // self-host → owner = full, no audit

        IamPrincipal? principal = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == principalId,
            cancellationToken
        );
        bool allowed =
            principal is { IsActive: true }
            && (
                await EffectivePermissionsAsync(principalId, targetBroadcasterId, cancellationToken)
            ).Contains(permissionKey);
        IamOutcome outcome = allowed ? IamOutcome.Allowed : IamOutcome.Denied;

        db.IamAuditLogs.Add(
            new()
            {
                PrincipalId = principalId,
                PrincipalType = principal?.PrincipalType ?? IamPrincipalType.Employee,
                Permission = permissionKey,
                TargetBroadcasterId = targetBroadcasterId,
                TargetResource = targetResource,
                BreakGlass = breakGlass,
                Justification = justification,
                Outcome = outcome,
                OccurredAt = clock.GetUtcNow().UtcDateTime,
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new IamAccessEvaluatedEvent
            {
                BroadcasterId = targetBroadcasterId ?? Guid.Empty,
                PrincipalId = principalId,
                Permission = permissionKey,
                TargetBroadcasterId = targetBroadcasterId,
                BreakGlass = breakGlass,
                Outcome = outcome,
            },
            cancellationToken
        );
        return Result.Success(allowed);
    }

    public async Task<Result<IamPrincipalDto?>> ResolvePrincipalAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        IamPrincipal? principal = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.UserId == userId,
            cancellationToken
        );
        return Result.Success(principal is null ? null : ToDto(principal));
    }

    public async Task<Result<IamPrincipalDto>> CreatePrincipalAsync(
        Guid actingPrincipalId,
        CreatePrincipalRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !await HasPermissionAsync(
                actingPrincipalId,
                CreatePrincipalPermission,
                null,
                cancellationToken
            )
        )
            return Result.Failure<IamPrincipalDto>("Requires iam:principal:create.", "FORBIDDEN");

        if (request.PrincipalType == IamPrincipalType.Employee && request.UserId is null)
            return Result.Failure<IamPrincipalDto>(
                "An employee principal requires a user id.",
                "VALIDATION_FAILED"
            );

        // Validate BEFORE tracking anything: this context is scoped per request, so an entity added to
        // the change tracker survives past a failed return and can flush as an orphan on a later,
        // unrelated SaveChangesAsync call in the same scope. Resolving the backing user first means an
        // unknown user never gets a principal (or a role assignment) tracked at all.
        User? user = null;
        if (request.PrincipalType == IamPrincipalType.Employee)
        {
            user = await db.Users.FirstOrDefaultAsync(
                u => u.Id == request.UserId,
                cancellationToken
            );
            if (user is null)
                return Result.Failure<IamPrincipalDto>("Unknown user.", "NOT_FOUND");
        }

        string? serviceAccountKey = null;
        IamPrincipal principal = new()
        {
            PrincipalType = request.PrincipalType,
            UserId = request.UserId,
            Name = request.DisplayName,
            IsActive = true,
        };
        if (request.PrincipalType == IamPrincipalType.ServiceAccount)
        {
            serviceAccountKey = GenerateServiceAccountKey();
            principal.ServiceAccountKeyHash = HashKey(serviceAccountKey);
        }
        db.IamPrincipals.Add(principal);

        // The promote wiring (roles-permissions §5.4): the platform-principal marker is what mints the
        // `admin` role claim on the next token refresh — without it the new principal could never enter
        // Plane-C (the authorization handler gates entry on that claim before consulting this service).
        if (user is not null)
            user.IsPlatformPrincipal = true;

        foreach (Guid roleId in request.RoleIds.Distinct())
            db.IamRoleAssignments.Add(
                new()
                {
                    PrincipalId = principal.Id,
                    RoleId = roleId,
                    AssignedByPrincipalId = actingPrincipalId,
                }
            );

        if (IsSaas)
            await AddAuditAsync(
                actingPrincipalId,
                CreatePrincipalPermission,
                targetPrincipalId: principal.Id,
                roleId: null,
                scopeChannelId: null,
                reason: null,
                cancellationToken
            );

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(principal) with { ServiceAccountKey = serviceAccountKey });
    }

    public async Task<Result<IamRoleAssignmentDto>> AssignRoleAsync(
        Guid actingPrincipalId,
        Guid principalId,
        Guid roleId,
        Guid? scopeChannelId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !await HasPermissionAsync(
                actingPrincipalId,
                ManagePermission,
                scopeChannelId,
                cancellationToken
            )
        )
            return Result.Failure<IamRoleAssignmentDto>("Requires iam:manage.", "FORBIDDEN");

        IamRole? role = await db.IamRoles.FirstOrDefaultAsync(
            r => r.Id == roleId,
            cancellationToken
        );
        if (role is null)
            return Result.Failure<IamRoleAssignmentDto>("Unknown role.", "NOT_FOUND");

        IamPrincipal? target = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == principalId,
            cancellationToken
        );
        if (target is null)
            return Result.Failure<IamRoleAssignmentDto>("Unknown principal.", "NOT_FOUND");
        if (!target.IsActive)
            return Result.Failure<IamRoleAssignmentDto>(
                "Cannot assign a role to an inactive principal.",
                "TARGET_INACTIVE"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        bool duplicate = await db.IamRoleAssignments.AnyAsync(
            a =>
                a.PrincipalId == principalId
                && a.RoleId == roleId
                && a.ScopeChannelId == scopeChannelId
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now),
            cancellationToken
        );
        if (duplicate)
            return Result.Failure<IamRoleAssignmentDto>(
                "This role is already assigned to the principal in that scope.",
                "DUPLICATE_ASSIGNMENT"
            );

        IamRoleAssignment assignment = new()
        {
            PrincipalId = principalId,
            RoleId = roleId,
            ScopeChannelId = scopeChannelId,
            AssignedByPrincipalId = actingPrincipalId,
            ExpiresAt = expiresAt,
            Reason = reason,
        };
        db.IamRoleAssignments.Add(assignment);

        if (IsSaas)
            await AddAuditAsync(
                actingPrincipalId,
                ManagePermission,
                targetPrincipalId: principalId,
                roleId: roleId,
                scopeChannelId: scopeChannelId,
                reason: reason,
                cancellationToken
            );

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(assignment, role.Name));
    }

    public async Task<Result> RevokeAssignmentAsync(
        Guid actingPrincipalId,
        Guid assignmentId,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        if (!await HasPermissionAsync(actingPrincipalId, ManagePermission, null, cancellationToken))
            return Result.Failure("Requires iam:manage.", "FORBIDDEN");

        IamRoleAssignment? assignment = await db.IamRoleAssignments.FirstOrDefaultAsync(
            a => a.Id == assignmentId && a.RevokedAt == null,
            cancellationToken
        );
        if (assignment is null)
            return Result.Success();

        // The lockout guard: revoking the last active grant of iam:manage would leave nobody able to
        // administer IAM at all. Only refuse when this specific assignment's role actually grants
        // iam:manage AND no OTHER active assignment (any principal) would still grant it afterward.
        if ((await ManagerRoleIdsAsync(cancellationToken)).Contains(assignment.RoleId))
        {
            int remainingHolders = await CountActiveManageHoldersAsync(
                cancellationToken,
                excludeAssignmentId: assignment.Id
            );
            if (remainingHolders == 0)
                return Result.Failure(
                    "Cannot revoke the last active grant of iam:manage.",
                    "LAST_MANAGER"
                );
        }

        assignment.RevokedAt = clock.GetUtcNow().UtcDateTime;
        assignment.Reason = reason ?? assignment.Reason;

        if (IsSaas)
            await AddAuditAsync(
                actingPrincipalId,
                ManagePermission,
                targetPrincipalId: assignment.PrincipalId,
                roleId: assignment.RoleId,
                scopeChannelId: assignment.ScopeChannelId,
                reason: reason,
                cancellationToken
            );

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(
        Guid principalId,
        Guid? scopeChannelId,
        CancellationToken cancellationToken = default
    ) =>
        Result.Success(
            await EffectivePermissionsAsync(principalId, scopeChannelId, cancellationToken)
        );

    public async Task<Result<IReadOnlyList<IamRoleDto>>> ListRolesAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<IamRole> roles = await db.IamRoles.OrderBy(r => r.Name).ToListAsync(cancellationToken);

        // Role → permission-key bundle, resolved in two set queries (never per role).
        List<(Guid RoleId, string Key)> rolePermissionKeys = (
            await db
                .IamRolePermissions.Join(
                    db.IamPermissions,
                    rp => rp.PermissionId,
                    p => p.Id,
                    (rp, p) => new { rp.RoleId, p.Key }
                )
                .ToListAsync(cancellationToken)
        )
            .Select(x => (x.RoleId, x.Key))
            .ToList();

        IReadOnlyList<IamRoleDto> dtos =
        [
            .. roles.Select(r => new IamRoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                [.. rolePermissionKeys.Where(rp => rp.RoleId == r.Id).Select(rp => rp.Key).Order()]
            )),
        ];
        return Result.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<IamPrincipalSummaryDto>>> ListPrincipalsAsync(
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<IamPrincipal> principals = await db
            .IamPrincipals.OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        List<(IamRoleAssignment Assignment, string RoleName)> active = (
            await db
                .IamRoleAssignments.Where(a =>
                    a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now)
                )
                .Join(
                    db.IamRoles,
                    a => a.RoleId,
                    r => r.Id,
                    (a, r) => new { Assignment = a, RoleName = r.Name }
                )
                .ToListAsync(cancellationToken)
        )
            .Select(x => (x.Assignment, x.RoleName))
            .ToList();

        IReadOnlyList<IamPrincipalSummaryDto> dtos =
        [
            .. principals.Select(p => new IamPrincipalSummaryDto(
                p.Id,
                p.PrincipalType,
                p.UserId,
                p.Name,
                p.IsActive,
                p.ExpiresAt,
                [
                    .. active
                        .Where(a => a.Assignment.PrincipalId == p.Id)
                        .Select(a => ToDto(a.Assignment, a.RoleName)),
                ]
            )),
        ];
        return Result.Success(dtos);
    }

    public async Task<Result> DeactivatePrincipalAsync(
        Guid actingPrincipalId,
        Guid principalId,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        if (!await HasPermissionAsync(actingPrincipalId, ManagePermission, null, cancellationToken))
            return Result.Failure("Requires iam:manage.", "FORBIDDEN");

        // The lockout guard: nobody deactivates themself — someone with iam:manage must always remain.
        if (actingPrincipalId == principalId)
            return Result.Failure("A principal cannot deactivate itself.", "VALIDATION_FAILED");

        IamPrincipal? principal = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == principalId,
            cancellationToken
        );
        if (principal is null)
            return Result.Failure("Unknown principal.", "NOT_FOUND");

        // The capability lockout guard: whoever is being deactivated must not be the LAST active holder
        // of iam:manage — that would strand the platform with nobody able to administer IAM at all. This
        // is broader than the self-deactivation guard above (which only catches the acting principal
        // deactivating themself); a manager can just as easily lock everyone out by deactivating the
        // last OTHER holder.
        if (
            await IsActiveManageHolderAsync(principalId, cancellationToken)
            && await CountActiveManageHoldersAsync(
                cancellationToken,
                excludePrincipalId: principalId
            ) == 0
        )
            return Result.Failure(
                "Cannot deactivate the last active holder of iam:manage.",
                "LAST_MANAGER"
            );

        principal.IsActive = false;
        await SetUserPlatformMarkerAsync(principal, isPlatformPrincipal: false, cancellationToken);

        if (IsSaas)
            await AddAuditAsync(
                actingPrincipalId,
                ManagePermission,
                targetPrincipalId: principalId,
                roleId: null,
                scopeChannelId: null,
                reason: reason,
                cancellationToken
            );

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReactivatePrincipalAsync(
        Guid actingPrincipalId,
        Guid principalId,
        CancellationToken cancellationToken = default
    )
    {
        if (!await HasPermissionAsync(actingPrincipalId, ManagePermission, null, cancellationToken))
            return Result.Failure("Requires iam:manage.", "FORBIDDEN");

        IamPrincipal? principal = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == principalId,
            cancellationToken
        );
        if (principal is null)
            return Result.Failure("Unknown principal.", "NOT_FOUND");

        principal.IsActive = true;
        await SetUserPlatformMarkerAsync(principal, isPlatformPrincipal: true, cancellationToken);

        if (IsSaas)
            await AddAuditAsync(
                actingPrincipalId,
                ManagePermission,
                targetPrincipalId: principalId,
                roleId: null,
                scopeChannelId: null,
                reason: null,
                cancellationToken
            );

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Mirrors an employee principal's active state onto the backing user's Plane-C entry marker
    /// (the `admin` role claim source) — the demote/repromote half of the §5.4 promote wiring.</summary>
    private async Task SetUserPlatformMarkerAsync(
        IamPrincipal principal,
        bool isPlatformPrincipal,
        CancellationToken ct
    )
    {
        if (principal.PrincipalType != IamPrincipalType.Employee || principal.UserId is null)
            return;

        User? user = await db.Users.FirstOrDefaultAsync(u => u.Id == principal.UserId, ct);
        if (user is not null)
            user.IsPlatformPrincipal = isPlatformPrincipal;
    }

    public Task<bool> IsSaasDeploymentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsSaas);

    /// <summary>
    /// Deployment-mode fact (fix D2), read once from the DI-resolved <see cref="DeploymentContext"/> — never
    /// derived from row counts. Self-host is implicitly-full regardless of how many <c>IamPrincipal</c> rows
    /// exist (the owner now has a real one from bootstrap); only SaaS enforces + audits.
    /// </summary>
    private bool IsSaas => deploymentContext.Mode == DeploymentMode.Saas;

    private async Task<bool> HasPermissionAsync(
        Guid principalId,
        string permissionKey,
        Guid? scopeChannelId,
        CancellationToken ct
    )
    {
        if (!IsSaas)
            return true; // self-host → owner = full
        return (await EffectivePermissionsAsync(principalId, scopeChannelId, ct)).Contains(
            permissionKey
        );
    }

    private async Task<IReadOnlyList<string>> EffectivePermissionsAsync(
        Guid principalId,
        Guid? scopeChannelId,
        CancellationToken ct
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<Guid> roleIds = await db
            .IamRoleAssignments.Where(a =>
                a.PrincipalId == principalId
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now)
                && (a.ScopeChannelId == null || a.ScopeChannelId == scopeChannelId)
            )
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(ct);
        if (roleIds.Count == 0)
            return [];

        List<Guid> permissionIds = await db
            .IamRolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync(ct);
        if (permissionIds.Count == 0)
            return [];

        return await db
            .IamPermissions.Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Key)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Every role that carries <c>iam:manage</c> — the pool consulted by the last-holder lockout
    /// guards on revoke/deactivate.</summary>
    private async Task<List<Guid>> ManagerRoleIdsAsync(CancellationToken ct) =>
        await db
            .IamRolePermissions.Join(
                db.IamPermissions,
                rp => rp.PermissionId,
                p => p.Id,
                (rp, p) => new { rp.RoleId, p.Key }
            )
            .Where(x => x.Key == ManagePermission)
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// The number of distinct ACTIVE principals still holding <c>iam:manage</c> through an active,
    /// non-expired role assignment, optionally excluding one principal (deactivate) or one specific
    /// assignment (revoke) from the count — so the caller can ask "if this went away, would anyone be
    /// left?" without a race between reading and acting.
    /// </summary>
    private async Task<int> CountActiveManageHoldersAsync(
        CancellationToken ct,
        Guid? excludePrincipalId = null,
        Guid? excludeAssignmentId = null
    )
    {
        List<Guid> managerRoleIds = await ManagerRoleIdsAsync(ct);
        if (managerRoleIds.Count == 0)
            return 0;

        DateTime now = clock.GetUtcNow().UtcDateTime;
        return await db
            .IamRoleAssignments.Where(a =>
                managerRoleIds.Contains(a.RoleId)
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now)
                && (excludePrincipalId == null || a.PrincipalId != excludePrincipalId)
                && (excludeAssignmentId == null || a.Id != excludeAssignmentId)
            )
            .Join(
                db.IamPrincipals,
                a => a.PrincipalId,
                p => p.Id,
                (a, p) => new { a.PrincipalId, p.IsActive }
            )
            .Where(x => x.IsActive)
            .Select(x => x.PrincipalId)
            .Distinct()
            .CountAsync(ct);
    }

    /// <summary>Does this active principal currently hold <c>iam:manage</c> through some active, non-expired
    /// assignment? Used to short-circuit the deactivate lockout guard for principals that never held it.</summary>
    private async Task<bool> IsActiveManageHolderAsync(Guid principalId, CancellationToken ct)
    {
        List<Guid> managerRoleIds = await ManagerRoleIdsAsync(ct);
        if (managerRoleIds.Count == 0)
            return false;
        if (!await db.IamPrincipals.AnyAsync(p => p.Id == principalId && p.IsActive, ct))
            return false;

        DateTime now = clock.GetUtcNow().UtcDateTime;
        return await db.IamRoleAssignments.AnyAsync(
            a =>
                a.PrincipalId == principalId
                && managerRoleIds.Contains(a.RoleId)
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now),
            ct
        );
    }

    /// <summary>
    /// Appends one <c>IamAuditLog</c> row for a management mutation (assign/revoke/create/deactivate/
    /// reactivate) — SaaS-only, per the entity's append-only, SaaS-only contract. The acting principal's
    /// type is looked up fresh so the row records who actually did it, matching the shape
    /// <see cref="AuthorizePlatformAsync"/> already writes for access evaluations.
    /// </summary>
    private async Task AddAuditAsync(
        Guid actingPrincipalId,
        string permission,
        Guid? targetPrincipalId,
        Guid? roleId,
        Guid? scopeChannelId,
        string? reason,
        CancellationToken ct
    )
    {
        IamPrincipal? actor = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == actingPrincipalId,
            ct
        );
        db.IamAuditLogs.Add(
            new()
            {
                PrincipalId = actingPrincipalId,
                PrincipalType = actor?.PrincipalType ?? IamPrincipalType.Employee,
                Permission = permission,
                TargetPrincipalId = targetPrincipalId,
                RoleId = roleId,
                TargetBroadcasterId = scopeChannelId,
                Justification = reason,
                Outcome = IamOutcome.Allowed,
                OccurredAt = clock.GetUtcNow().UtcDateTime,
            }
        );
    }

    private static string GenerateServiceAccountKey() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static IamPrincipalDto ToDto(IamPrincipal p) =>
        new(p.Id, p.PrincipalType, p.UserId, p.Name, p.IsActive, p.ExpiresAt);

    private static IamRoleAssignmentDto ToDto(IamRoleAssignment a, string roleName) =>
        new(
            a.Id,
            a.PrincipalId,
            a.RoleId,
            roleName,
            a.ScopeChannelId,
            a.ExpiresAt,
            a.RevokedAt,
            a.Reason,
            a.CreatedAt
        );
}
