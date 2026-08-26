// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
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
    IJwtTokenService jwt,
    IEventBus eventBus,
    TimeProvider clock,
    ISessionRevocationService sessionRevocation
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

        // The target's own broadcaster channel scopes the `tenant` claim, exactly like the target's own login
        // — null when the target owns no channel (a plain viewer).
        Guid? tenantId = await db
            .Channels.Where(c => c.OwnerUserId == targetUserId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // The acting operator is named ONLY on the non-authoritative `act` claim, never as a role.
        IamPrincipal? actor = await db.IamPrincipals.FirstOrDefaultAsync(
            p => p.Id == actingPrincipalId,
            ct
        );
        string actorUserId = (actor?.UserId ?? actingPrincipalId).ToString();

        // CRITICAL INVARIANT: roles + identity are the TARGET's, computed the same way SessionService.RolesFor
        // does for a normal login. The operator's `admin` role is NEVER carried onto an impersonation token —
        // an access-only token (no refresh) that grants exactly the impersonated user's access. `sid` is the
        // GRANT id itself: ending the grant (EndImpersonationAsync) revokes this exact session, and the
        // token's lifetime is clamped to never outlive the grant.
        string accessToken = jwt.GenerateAccessToken(
            target.Id,
            target.Username,
            tenantId,
            accessGrantId,
            RolesFor(target),
            idp: target.Platform,
            actorUserId: actorUserId,
            actorUsername: actor?.Name,
            maxExpiresAt: grant.ExpiresAt
        );

        DateTime expiresAt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo;

        await eventBus.PublishAsync(
            new ImpersonationStartedEvent
            {
                BroadcasterId = Guid.Empty,
                OperatorPrincipalId = actingPrincipalId,
                TargetUserId = targetUserId,
                AccessGrantId = accessGrantId,
                ExpiresAt = expiresAt,
            },
            ct
        );

        return Result.Success(
            new ImpersonationTokenDto(accessToken, expiresAt, accessGrantId, ToDto(target))
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
    /// The role set an access token carries for <paramref name="user"/> — identical to
    /// <c>SessionService.RolesFor</c>, the normal-login source of truth. Reused verbatim for the TARGET of an
    /// impersonation so the minted token grants exactly the impersonated user's access, never the operator's.
    /// </summary>
    private static IEnumerable<string> RolesFor(User user) =>
        user.IsPlatformPrincipal ? ["user", "admin"] : ["user"];

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

    /// <summary>The impersonated user's profile, mirroring <c>UserService.ToDto</c> (LastLoginAt = UpdatedAt).</summary>
    private static UserDto ToDto(User u) =>
        new(
            u.Id.ToString(),
            u.Username,
            u.DisplayName,
            u.ProfileImageUrl,
            null,
            u.CreatedAt,
            u.UpdatedAt
        );

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
