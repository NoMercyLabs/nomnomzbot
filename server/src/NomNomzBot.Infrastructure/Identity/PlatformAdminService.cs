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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Billing.Entities;
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
    IJwtTokenService jwt,
    IEventBus eventBus,
    TimeProvider clock,
    ISessionRevocationService sessionRevocation,
    IChannelDeletePreviewService channelDeletePreview,
    IChannelService channelService,
    IDatabaseMigrator migrator,
    ITenantMemberDirectoryService memberDirectory
) : IPlatformAdminService
{
    /// <summary>The seeded role a support-access grant assigns, narrowed to the target tenant (§3.2).</summary>
    private const string SupportRoleName = "platform-support";

    /// <summary>
    /// Default lifetime for a support-access grant when the request omits one. A grant with no expiry never
    /// expires — <see cref="StartImpersonationAsync"/> requires a non-null, still-future <c>ExpiresAt</c>, so
    /// an omitted expiry must still resolve to a real, bounded one, never to "permanent" (a security defect in
    /// its own right: the banner promises "temporary support access").
    /// </summary>
    private static readonly TimeSpan DefaultTenantAccessDuration = TimeSpan.FromHours(4);

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

        // An operator suspending their own channel locks themselves out of the very console that could
        // reinstate it (the channel-scoped surface goes dark for a suspended tenant). Another operator
        // has to do it.
        Guid? actingUserId = await PrincipalUserIdAsync(principalId, ct);
        if (actingUserId is not null && actingUserId == channel.OwnerUserId)
            return Result.Failure(
                "You cannot suspend your own channel; another operator has to.",
                "SELF_SUSPEND"
            );

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
        await eventBus.PublishAsync(
            new ChannelSuspendedEvent
            {
                BroadcasterId = broadcasterId,
                Status = request.NewStatus,
                Reason = request.Reason,
                ActorUserId = actingUserId,
            },
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
        await eventBus.PublishAsync(
            new ChannelReinstatedEvent
            {
                BroadcasterId = broadcasterId,
                ActorUserId = await PrincipalUserIdAsync(principalId, ct),
            },
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
        List<Guid> superseded = await SupersedeOpenSupportSessionsAsync(
            principalId,
            broadcasterId,
            supportRole.Id,
            now,
            ct
        );

        DateTime expiresAt = request.ExpiresAt ?? now.Add(DefaultTenantAccessDuration);
        IamRoleAssignment assignment = new()
        {
            PrincipalId = principalId,
            RoleId = supportRole.Id,
            ScopeChannelId = broadcasterId,
            AssignedByPrincipalId = principalId,
            ExpiresAt = expiresAt,
            Reason = request.Justification,
        };
        db.IamRoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        // A token minted on a superseded session carries its id as `sid`; revoking it ends that token too.
        foreach (Guid sessionId in superseded)
            await sessionRevocation.RevokeAsync(sessionId, ct);

        await eventBus.PublishAsync(
            new TenantAccessGrantedEvent
            {
                BroadcasterId = Guid.Empty,
                PrincipalId = principalId,
                TargetBroadcasterId = broadcasterId,
                AccessGrantId = assignment.Id,
                BreakGlass = request.BreakGlass,
                ExpiresAt = expiresAt,
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
                expiresAt,
                RevokedAt: null
            )
        );
    }

    /// <summary>
    /// Ends the operator's own still-open support sessions on <paramref name="broadcasterId"/> (stamped, saved by
    /// the caller) and returns their ids. One operator holds at most one open support session per tenant: a
    /// session left open by an act-as that never reached Exit is closed by the next begin instead of stacking.
    /// </summary>
    private async Task<List<Guid>> SupersedeOpenSupportSessionsAsync(
        Guid principalId,
        Guid broadcasterId,
        Guid supportRoleId,
        DateTime now,
        CancellationToken ct
    )
    {
        List<IamRoleAssignment> open = await db
            .IamRoleAssignments.Where(a =>
                a.PrincipalId == principalId
                && a.RoleId == supportRoleId
                && a.ScopeChannelId == broadcasterId
                && a.RevokedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now)
            )
            .ToListAsync(ct);

        foreach (IamRoleAssignment session in open)
            session.RevokedAt = now;
        return open.Select(a => a.Id).ToList();
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

    public async Task<Result<ImpersonationTokenDto>> StartImpersonationAsync(
        Guid actingPrincipalId,
        Guid targetUserId,
        Guid accessGrantId,
        string justification,
        CancellationToken ct = default
    )
    {
        // Deployment mode does NOT gate this. Impersonation is guarded by what actually makes it safe — the
        // `user:impersonate` permission, an open time-boxed support grant, a mandatory justification, an audit
        // row and a revocable session — and those hold identically on self-host, where the operator is the
        // instance owner acting on their own deployment.

        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<ImpersonationTokenDto>(
                "A justification is required to impersonate a user.",
                "VALIDATION_FAILED"
            );

        // Gate + audit FIRST — the target user id AND the backing session ride the audit row
        // (TargetResource) in one row, so a single audit query names WHO impersonated WHOM under WHICH
        // session, whether the mint goes on to succeed or fails the session check below. A permission
        // denial short-circuits before any token is minted or the grant is even looked up.
        Result authorized = await RequireAsync(
            actingPrincipalId,
            "user:impersonate",
            null,
            justification,
            ct,
            targetResource: $"user:{targetUserId}|session:{accessGrantId}"
        );
        if (authorized.IsFailure)
            return authorized.WithValue<ImpersonationTokenDto>(null!);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IamRoleAssignment? grant = await db.IamRoleAssignments.FirstOrDefaultAsync(
            a =>
                a.Id == accessGrantId
                && a.PrincipalId == actingPrincipalId
                && a.RevokedAt == null
                && a.ExpiresAt != null
                && a.ExpiresAt > now,
            ct
        );
        if (grant is null)
            return Result.Failure<ImpersonationTokenDto>(
                "An open, time-boxed support session is required to impersonate a user.",
                "SESSION_REQUIRED"
            );

        User? target = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null)
            return Result.Failure<ImpersonationTokenDto>("Unknown user.", "NOT_FOUND");

        // The support session names ONE tenant; it authorizes acting as that tenant's people only. Without this
        // a grant opened for tenant A would let the operator act as anyone, while the audit trail names A.
        if (
            grant.ScopeChannelId is not { } scopeChannelId
            || !await memberDirectory.IsMemberAsync(scopeChannelId, targetUserId, ct)
        )
            return Result.Failure<ImpersonationTokenDto>(
                "The support session does not cover this user: they do not belong to the session's channel.",
                "TARGET_OUTSIDE_SESSION"
            );

        // The target's identity, tenant and roles exactly as their own login mints them; the operator rides
        // only the non-authoritative `act` claim. An access token whose `sid` is the GRANT id: closing the grant
        // (EndImpersonationAsync, EndTenantAccessAsync, expiry) ends it, and it never outlives the grant.
        ImpersonationTokenDto token = await ImpersonationTokenMinter.MintAsync(
            db,
            jwt,
            grant,
            target,
            ct
        );

        await eventBus.PublishAsync(
            new ImpersonationStartedEvent
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = actingPrincipalId,
                TargetUserId = targetUserId,
                AccessGrantId = accessGrantId,
                ExpiresAt = token.ExpiresAt,
            },
            ct
        );

        return Result.Success(token);
    }

    public async Task<Result<PagedList<TenantMemberDto>>> ListTenantMembersAsync(
        Guid principalId,
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "tenant:read", broadcasterId, null, ct);
        if (authorized.IsFailure)
            return authorized.WithValue<PagedList<TenantMemberDto>>(null!);

        if (!await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct))
            return Result.Failure<PagedList<TenantMemberDto>>("Unknown tenant.", "NOT_FOUND");

        return Result.Success(
            await memberDirectory.ListAsync(broadcasterId, search, pagination, ct)
        );
    }

    public async Task<Result> EndImpersonationAsync(
        Guid actingPrincipalId,
        Guid accessGrantId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(
            actingPrincipalId,
            "user:impersonate",
            null,
            null,
            ct,
            targetResource: $"session:{accessGrantId}"
        );
        if (authorized.IsFailure)
            return authorized;

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IamRoleAssignment? grant = await db.IamRoleAssignments.FirstOrDefaultAsync(
            a => a.Id == accessGrantId && a.PrincipalId == actingPrincipalId && a.RevokedAt == null,
            ct
        );
        if (grant is null)
            return Result.Failure("No active support session of yours matches.", "NOT_FOUND");

        string? startResource = await db
            .IamAuditLogs.Where(l =>
                l.PrincipalId == actingPrincipalId
                && l.Permission == "user:impersonate"
                && l.TargetResource != null
                && l.TargetResource.Contains($"session:{accessGrantId}")
            )
            .OrderByDescending(l => l.OccurredAt)
            .Select(l => l.TargetResource)
            .FirstOrDefaultAsync(ct);
        Guid? targetUserId = ParseTargetUserId(startResource);

        grant.RevokedAt = now;
        await db.SaveChangesAsync(ct);

        // Revokes the exact `sid` the impersonation token carries — the SAME token fails authentication on
        // its very next request (S098b's revocation check), immediately, with no dependency on token expiry.
        await sessionRevocation.RevokeAsync(accessGrantId, ct);

        await eventBus.PublishAsync(
            new ImpersonationEndedEvent
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = actingPrincipalId,
                TargetUserId = targetUserId ?? Guid.Empty,
                AccessGrantId = accessGrantId,
            },
            ct
        );

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

    public async Task<Result<IReadOnlyList<TenantLimitOverrideDto>>> ListTenantLimitOverridesAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(principalId, "tenant:read", broadcasterId, null, ct);
        if (authorized.IsFailure)
            return authorized.WithValue<IReadOnlyList<TenantLimitOverrideDto>>(null!);

        List<TenantLimitOverrideDto> overrides = await db
            .TenantLimitOverrides.Where(o => o.BroadcasterId == broadcasterId)
            .OrderBy(o => o.LimitKey)
            .Select(o => new TenantLimitOverrideDto(
                o.Id,
                o.BroadcasterId,
                o.LimitKey,
                o.LimitValue,
                o.Reason,
                o.GrantedByPrincipalId,
                o.CreatedAt,
                o.ExpiresAt
            ))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TenantLimitOverrideDto>>(overrides);
    }

    public async Task<Result<TenantLimitOverrideDto>> SetTenantLimitOverrideAsync(
        Guid principalId,
        Guid broadcasterId,
        SetTenantLimitOverrideRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<TenantLimitOverrideDto>(
                "A reason is required to override a tenant's quota.",
                "VALIDATION_FAILED"
            );
        if (!LimitedResourceRegistry.TryGet(request.LimitKey, out _))
            return Result.Failure<TenantLimitOverrideDto>(
                $"'{request.LimitKey}' is not a declared limited resource.",
                "NOT_FOUND"
            );

        Result authorized = await RequireAsync(
            principalId,
            "tenant:quota:manage",
            broadcasterId,
            request.Reason,
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<TenantLimitOverrideDto>(null!);

        if (!await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct))
            return Result.Failure<TenantLimitOverrideDto>("Unknown tenant.", "NOT_FOUND");

        TenantLimitOverride? existing = await db.TenantLimitOverrides.FirstOrDefaultAsync(
            o => o.BroadcasterId == broadcasterId && o.LimitKey == request.LimitKey,
            ct
        );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            existing = new TenantLimitOverride
            {
                BroadcasterId = broadcasterId,
                LimitKey = request.LimitKey,
            };
            db.TenantLimitOverrides.Add(existing);
        }

        existing.LimitValue = request.LimitValue;
        existing.Reason = request.Reason;
        existing.GrantedByPrincipalId = principalId;
        existing.ExpiresAt = request.ExpiresAt;
        await db.SaveChangesAsync(ct);

        return Result.Success(
            new TenantLimitOverrideDto(
                existing.Id,
                existing.BroadcasterId,
                existing.LimitKey,
                existing.LimitValue,
                existing.Reason,
                existing.GrantedByPrincipalId,
                existing.CreatedAt == default ? now : existing.CreatedAt,
                existing.ExpiresAt
            )
        );
    }

    public async Task<Result> ClearTenantLimitOverrideAsync(
        Guid principalId,
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(
            principalId,
            "tenant:quota:manage",
            broadcasterId,
            null,
            ct
        );
        if (authorized.IsFailure)
            return authorized;

        TenantLimitOverride? existing = await db.TenantLimitOverrides.FirstOrDefaultAsync(
            o => o.BroadcasterId == broadcasterId && o.LimitKey == limitKey,
            ct
        );
        if (existing is null)
            return Result.Failure("No override for that tenant and limit key.", "NOT_FOUND");

        existing.DeletedAt = clock.GetUtcNow().UtcDateTime;
        existing.DeletedBy = principalId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<TenantRemigrationResultDto>> ForceRemigrationAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<TenantRemigrationResultDto>(
                "A justification is required to force a re-migration.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            principalId,
            "tenant:remigrate",
            broadcasterId,
            justification,
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<TenantRemigrationResultDto>(null!);

        IReadOnlyList<string> pendingBefore = await migrator.GetPendingMigrationsAsync(ct);
        await migrator.MigrateAsync(ct);
        IReadOnlyList<string> pendingAfter = await migrator.GetPendingMigrationsAsync(ct);

        List<string> applied = [.. pendingBefore.Except(pendingAfter, StringComparer.Ordinal)];
        return Result.Success(new TenantRemigrationResultDto(applied, pendingAfter));
    }

    public async Task<Result<ChannelDeletePreviewDto>> PreviewEraseTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(
            principalId,
            "tenant:erase",
            broadcasterId,
            null,
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<ChannelDeletePreviewDto>(null!);

        return await channelDeletePreview.PreviewAsync(broadcasterId.ToString(), ct);
    }

    public async Task<Result> EraseTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        string justification,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure(
                "A justification is required to erase a tenant.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            principalId,
            "tenant:erase",
            broadcasterId,
            justification,
            ct
        );
        if (authorized.IsFailure)
            return authorized;

        return await channelService.DeleteAsync(broadcasterId.ToString(), ct);
    }

    public async Task<Result<string>> ExportTenantAsync(
        Guid principalId,
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result authorized = await RequireAsync(
            principalId,
            "tenant:erase",
            broadcasterId,
            null,
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<string>(null!);

        if (!await db.Channels.AnyAsync(c => c.Id == broadcasterId, ct))
            return Result.Failure<string>("Unknown tenant.", "NOT_FOUND");

        return Result.Success(
            await TenantExport.BuildAsync(db, broadcasterId, clock.GetUtcNow().UtcDateTime, ct)
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
        bool breakGlass = false,
        string? targetResource = null
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            principalId,
            permissionKey,
            targetBroadcasterId,
            breakGlass,
            justification,
            ct,
            targetResource
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {permissionKey}.", "FORBIDDEN");
    }

    /// <summary>
    /// Recovers the target user id from the <c>"user:{id}|session:{id}"</c> <c>TargetResource</c> shape
    /// written by <see cref="StartImpersonationAsync"/>'s audit row — the only durable record linking a
    /// session id back to who was impersonated under it.
    /// </summary>
    private static Guid? ParseTargetUserId(string? targetResource)
    {
        if (string.IsNullOrEmpty(targetResource))
            return null;
        string[] parts = targetResource.Split('|');
        string? userPart = parts.FirstOrDefault(p =>
            p.StartsWith("user:", StringComparison.Ordinal)
        );
        return
            userPart is not null && Guid.TryParse(userPart.AsSpan("user:".Length), out Guid userId)
            ? userId
            : null;
    }

    private Task<Guid?> PrincipalUserIdAsync(Guid principalId, CancellationToken ct) =>
        db
            .IamPrincipals.Where(p => p.Id == principalId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(ct);

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
