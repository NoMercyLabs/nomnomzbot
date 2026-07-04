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
