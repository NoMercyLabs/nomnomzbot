// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Plane-C privileged tenant operations (stream-admin.md §3.2). Every method re-asserts its permission via
/// <see cref="IPlatformIamService.AuthorizePlatformAsync"/> — the call that also writes the audit row on SaaS
/// (self-host with zero principals short-circuits to allow, no audit). Suspension is ENFORCED, not decorative:
/// the bot lifecycle only serves <c>active</c> channels and tenant resolution rejects suspended tenants.
/// </summary>
public sealed class PlatformAdminService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    IEventBus eventBus,
    TimeProvider clock
) : IPlatformAdminService
{
    /// <summary>The seeded role a support-access grant assigns, narrowed to the target tenant (§3.2).</summary>
    private const string SupportRoleName = "platform-support";

    public async Task<Result<PagedList<AdminTenantDto>>> ListTenantsAsync(
        Guid principalId,
        AdminTenantQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "tenant:read", null, null, ct);
        if (authorized.IsFailure)
            return authorized.WithValue<PagedList<AdminTenantDto>>(null!);

        IQueryable<Channel> channels = db.Channels;
        if (!string.IsNullOrWhiteSpace(query.Search))
            channels = channels.Where(c =>
                c.NameNormalized.Contains(query.Search.ToLowerInvariant())
            );
        if (!string.IsNullOrWhiteSpace(query.Status))
            channels = channels.Where(c => c.Status == query.Status);
        if (query.IsLive is not null)
            channels = channels.Where(c => c.IsLive == query.IsLive);

        int total = await channels.CountAsync(ct);
        List<AdminTenantDto> items = await channels
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new AdminTenantDto(
                c.Id,
                c.Name,
                c.TwitchChannelId ?? "",
                c.Status,
                c.BillingTierKey,
                c.IsLive,
                c.CreatedAt,
                c.SuspendedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<AdminTenantDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<AdminTenantDetailDto>> GetTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "tenant:read", broadcasterId, null, ct);
        if (authorized.IsFailure)
            return authorized.WithValue<AdminTenantDetailDto>(null!);

        AdminTenantDetailDto? detail = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new AdminTenantDetailDto(
                c.Id,
                c.Name,
                c.TwitchChannelId ?? "",
                c.Status,
                c.SuspendedReason,
                c.BillingTierKey,
                c.DeploymentMode,
                c.OwnerUserId,
                c.User.DisplayName,
                db.ChannelMemberships.Count(m => m.BroadcasterId == c.Id),
                c.CreatedAt,
                c.SuspendedAt
            ))
            .FirstOrDefaultAsync(ct);

        return detail is null
            ? Result.Failure<AdminTenantDetailDto>("Unknown tenant.", "NOT_FOUND")
            : Result.Success(detail);
    }

    public async Task<Result> SuspendTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        SuspendTenantRequest request,
        CancellationToken ct = default
    )
    {
        if (
            request.NewStatus != AuthEnums.ChannelStatus.Suspended
            && request.NewStatus != AuthEnums.ChannelStatus.PlatformBanned
        )
            return Result.Failure(
                "NewStatus must be 'suspended' or 'platform_banned'.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            principalId,
            "tenant:suspend",
            broadcasterId,
            request.Reason,
            ct
        );
        if (authorized.IsFailure)
            return authorized;

        Channel? channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == broadcasterId, ct);
        if (channel is null)
            return Result.Failure("Unknown tenant.", "NOT_FOUND");

        channel.Status = request.NewStatus;
        channel.SuspendedAt = clock.GetUtcNow().UtcDateTime;
        channel.SuspendedReason = request.Reason;
        await db.SaveChangesAsync(ct);

        await PublishSuspensionChangedAsync(
            principalId,
            broadcasterId,
            request.NewStatus,
            request.Reason,
            ct
        );
        return Result.Success();
    }

    public async Task<Result> ReinstateTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(
            principalId,
            "tenant:suspend",
            broadcasterId,
            justification,
            ct
        );
        if (authorized.IsFailure)
            return authorized;

        Channel? channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == broadcasterId, ct);
        if (channel is null)
            return Result.Failure("Unknown tenant.", "NOT_FOUND");

        channel.Status = AuthEnums.ChannelStatus.Active;
        channel.SuspendedAt = null;
        channel.SuspendedReason = null;
        await db.SaveChangesAsync(ct);

        await PublishSuspensionChangedAsync(
            principalId,
            broadcasterId,
            AuthEnums.ChannelStatus.Active,
            justification,
            ct
        );
        return Result.Success();
    }

    public async Task<Result<TenantAccessGrantDto>> BeginTenantAccessAsync(
        Guid principalId,
        Guid broadcasterId,
        BeginTenantAccessRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Justification))
            return Result.Failure<TenantAccessGrantDto>(
                "A justification is required for tenant access.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            principalId,
            "tenant:access",
            broadcasterId,
            request.Justification,
            ct,
            request.BreakGlass
        );
        if (authorized.IsFailure)
            return authorized.WithValue<TenantAccessGrantDto>(null!);

        if (!await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct))
            return Result.Failure<TenantAccessGrantDto>("Unknown tenant.", "NOT_FOUND");

        IamRole? supportRole = await db.IamRoles.FirstOrDefaultAsync(
            r => r.Name == SupportRoleName,
            ct
        );
        if (supportRole is null)
            return Result.Failure<TenantAccessGrantDto>(
                "The platform-support role is not seeded.",
                "NOT_FOUND"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IamRoleAssignment assignment = new()
        {
            PrincipalId = principalId,
            RoleId = supportRole.Id,
            ScopeChannelId = broadcasterId,
            AssignedByPrincipalId = principalId,
            ExpiresAt = request.ExpiresAt,
            Reason = request.Justification,
        };
        db.IamRoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new TenantAccessGrantedEvent
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = principalId,
                TargetBroadcasterId = broadcasterId,
                AccessGrantId = assignment.Id,
                BreakGlass = request.BreakGlass,
                ExpiresAt = request.ExpiresAt,
            },
            ct
        );

        return Result.Success(
            new TenantAccessGrantDto(
                assignment.Id,
                principalId,
                broadcasterId,
                request.Justification,
                request.BreakGlass,
                now,
                request.ExpiresAt,
                RevokedAt: null
            )
        );
    }

    public async Task<Result> EndTenantAccessAsync(
        Guid principalId,
        Guid accessGrantId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "tenant:access", null, null, ct);
        if (authorized.IsFailure)
            return authorized;

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IamRoleAssignment? assignment = await db.IamRoleAssignments.FirstOrDefaultAsync(
            a =>
                a.Id == accessGrantId
                && a.PrincipalId == principalId
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now),
            ct
        );
        if (assignment is null)
            return Result.Failure("No active access grant of yours matches.", "NOT_FOUND");

        assignment.RevokedAt = now;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<IamAuditEntryDto>>> SearchAuditAsync(
        Guid principalId,
        AuditSearchQuery query,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "audit:read", null, null, ct);
        if (authorized.IsFailure)
            return authorized.WithValue<PagedList<IamAuditEntryDto>>(null!);

        IQueryable<IamAuditLog> logs = db.IamAuditLogs;
        if (query.PrincipalId is not null)
            logs = logs.Where(l => l.PrincipalId == query.PrincipalId);
        if (query.TargetBroadcasterId is not null)
            logs = logs.Where(l => l.TargetBroadcasterId == query.TargetBroadcasterId);
        if (!string.IsNullOrWhiteSpace(query.Permission))
            logs = logs.Where(l => l.Permission == query.Permission);
        if (
            !string.IsNullOrWhiteSpace(query.Outcome)
            && Enum.TryParse(query.Outcome, true, out IamOutcome outcome)
        )
            logs = logs.Where(l => l.Outcome == outcome);
        if (query.From is not null)
            logs = logs.Where(l => l.OccurredAt >= query.From);
        if (query.To is not null)
            logs = logs.Where(l => l.OccurredAt <= query.To);

        int total = await logs.CountAsync(ct);
        List<IamAuditEntryDto> items = await logs.OrderByDescending(l => l.OccurredAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(l => new IamAuditEntryDto(
                l.Id,
                l.PrincipalId,
                l.PrincipalType.ToString(),
                l.Permission,
                l.TargetBroadcasterId,
                l.TargetResource,
                l.Justification,
                l.BreakGlass,
                l.Outcome.ToString(),
                l.OccurredAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<IamAuditEntryDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    /// <summary>
    /// The one authorization funnel: <see cref="IPlatformIamService.AuthorizePlatformAsync"/> both decides AND
    /// audits (allowed or denied) on SaaS; a denial maps to <c>FORBIDDEN</c> here.
    /// </summary>
    private async Task<Result> RequireAsync(
        Guid principalId,
        string permissionKey,
        Guid? targetBroadcasterId,
        string? justification,
        CancellationToken ct,
        bool breakGlass = false
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            principalId,
            permissionKey,
            targetBroadcasterId,
            breakGlass,
            justification,
            ct
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {permissionKey}.", "FORBIDDEN");
    }

    private Task PublishSuspensionChangedAsync(
        Guid principalId,
        Guid broadcasterId,
        string newStatus,
        string? reason,
        CancellationToken ct
    ) =>
        eventBus.PublishAsync(
            new TenantSuspensionChangedEvent
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = principalId,
                TargetBroadcasterId = broadcasterId,
                NewStatus = newStatus,
                Reason = reason,
            },
            ct
        );
}
