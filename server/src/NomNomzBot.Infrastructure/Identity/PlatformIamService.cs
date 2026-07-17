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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Plane-C platform IAM (roles-permissions §3.7). On self-host no <c>IamPrincipal</c>s exist, so every
/// authorize short-circuits to true with no audit (the operator is implicitly full). On SaaS, a principal's
/// effective permissions are the union over its active, non-expired, in-scope role assignments, every
/// authorize is written to the append-only audit log, and management ops are themselves gated on iam:* keys.
/// </summary>
public sealed class PlatformIamService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
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
        CancellationToken cancellationToken = default
    )
    {
        if (!await IsSaasAsync(cancellationToken))
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
            new IamAuditLog
            {
                PrincipalId = principalId,
                PrincipalType = principal?.PrincipalType ?? IamPrincipalType.Employee,
                Permission = permissionKey,
                TargetBroadcasterId = targetBroadcasterId,
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
        if (request.PrincipalType == IamPrincipalType.Employee)
        {
            User? user = await db.Users.FirstOrDefaultAsync(
                u => u.Id == request.UserId,
                cancellationToken
            );
            if (user is null)
                return Result.Failure<IamPrincipalDto>("Unknown user.", "NOT_FOUND");
            user.IsPlatformPrincipal = true;
        }

        foreach (Guid roleId in request.RoleIds.Distinct())
            db.IamRoleAssignments.Add(
                new IamRoleAssignment
                {
                    PrincipalId = principal.Id,
                    RoleId = roleId,
                    AssignedByPrincipalId = actingPrincipalId,
                }
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
        if (!await db.IamPrincipals.AnyAsync(p => p.Id == principalId, cancellationToken))
            return Result.Failure<IamRoleAssignmentDto>("Unknown principal.", "NOT_FOUND");

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

        assignment.RevokedAt = clock.GetUtcNow().UtcDateTime;
        assignment.Reason = reason ?? assignment.Reason;
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

        principal.IsActive = false;
        await SetUserPlatformMarkerAsync(principal, isPlatformPrincipal: false, cancellationToken);
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

    public async Task<bool> HasAnyPrincipalsAsync(CancellationToken cancellationToken = default) =>
        await IsSaasAsync(cancellationToken);

    private async Task<bool> IsSaasAsync(CancellationToken ct) =>
        await db.IamPrincipals.AnyAsync(ct);

    private async Task<bool> HasPermissionAsync(
        Guid principalId,
        string permissionKey,
        Guid? scopeChannelId,
        CancellationToken ct
    )
    {
        if (!await IsSaasAsync(ct))
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
