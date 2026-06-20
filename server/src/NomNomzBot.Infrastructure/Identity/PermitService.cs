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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// <c>!permit</c> / <c>!unpermit</c> temporary delegation (roles-permissions §3.6). Role grants are bounded by
/// no-escalation (≤ grantor's level); capability grants are default-deny (the action must be permit-grantable)
/// AND no-escalation (the grantor must hold the action themselves — so a Critical capability can only be
/// delegated by someone who already clears its floor). Revocation marks <c>RevokedAt</c>; an expiry sweep
/// revokes due grants across all channels.
/// </summary>
public sealed class PermitService(
    IApplicationDbContext db,
    IRoleResolver roleResolver,
    IEventBus eventBus,
    TimeProvider clock
) : IPermitService
{
    public async Task<Result<PermitGrantDto>> GrantRoleAsync(
        Guid broadcasterId,
        Guid targetUserId,
        ManagementRole role,
        Guid grantedByUserId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        Result<int> grantorLevel = await roleResolver.ResolveEffectiveLevelAsync(
            grantedByUserId,
            broadcasterId,
            cancellationToken
        );
        if ((grantorLevel.IsSuccess ? grantorLevel.Value : 0) < role.ToLevel())
            return Result.Failure<PermitGrantDto>(
                "Cannot permit a role above your own level.",
                "FORBIDDEN"
            );

        PermitGrant grant = new()
        {
            BroadcasterId = broadcasterId,
            UserId = targetUserId,
            GrantType = PermitGrantType.Role,
            GrantedRole = role,
            GrantedByUserId = grantedByUserId,
            ExpiresAt = expiresAt,
            Reason = reason,
        };
        db.PermitGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);

        await PublishGrantedAsync(grant, capabilityActionKey: null, cancellationToken);
        string? username = await LookupUsernameAsync(targetUserId, cancellationToken);
        return Result.Success(ToDto(grant, username, capabilityActionKey: null));
    }

    public async Task<Result<PermitGrantDto>> GrantCapabilityAsync(
        Guid broadcasterId,
        Guid targetUserId,
        string actionKey,
        Guid grantedByUserId,
        DateTime? expiresAt,
        string? reason,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await db.ActionDefinitions.FirstOrDefaultAsync(
            a => a.ActionKey == actionKey,
            cancellationToken
        );
        if (action is null)
            return Result.Failure<PermitGrantDto>($"Unknown action '{actionKey}'.", "FORBIDDEN");
        if (!action.IsGrantableViaPermit)
            return Result.Failure<PermitGrantDto>(
                $"Action '{actionKey}' is not delegable via permit.",
                "FORBIDDEN"
            );

        Result<bool> grantorHolds = await roleResolver.HasCapabilityAsync(
            grantedByUserId,
            broadcasterId,
            actionKey,
            cancellationToken
        );
        if (!grantorHolds.IsSuccess || !grantorHolds.Value)
            return Result.Failure<PermitGrantDto>(
                "You cannot delegate a capability you do not hold.",
                "FORBIDDEN"
            );

        PermitGrant grant = new()
        {
            BroadcasterId = broadcasterId,
            UserId = targetUserId,
            GrantType = PermitGrantType.Capability,
            ActionDefinitionId = action.Id,
            GrantedByUserId = grantedByUserId,
            ExpiresAt = expiresAt,
            Reason = reason,
        };
        db.PermitGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);

        await PublishGrantedAsync(grant, actionKey, cancellationToken);
        string? username = await LookupUsernameAsync(targetUserId, cancellationToken);
        return Result.Success(ToDto(grant, username, actionKey));
    }

    public async Task<Result> RevokeAsync(
        Guid broadcasterId,
        Guid targetUserId,
        string? actionKeyOrRole,
        Guid revokedByUserId,
        CancellationToken cancellationToken = default
    )
    {
        List<PermitGrant> active = await db
            .PermitGrants.Where(p =>
                p.BroadcasterId == broadcasterId
                && p.UserId == targetUserId
                && p.RevokedAt == null
                && p.DeletedAt == null
            )
            .ToListAsync(cancellationToken);

        List<PermitGrant> toRevoke = await SelectForRevocationAsync(
            active,
            actionKeyOrRole,
            cancellationToken
        );
        if (toRevoke.Count == 0)
            return Result.Success();

        DateTime now = clock.GetUtcNow().UtcDateTime;
        foreach (PermitGrant grant in toRevoke)
            grant.RevokedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        foreach (PermitGrant grant in toRevoke)
            await PublishRevokedAsync(grant, revokedByUserId, "unpermit", cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PermitGrantDto>>> ListActiveGrantsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<PermitGrant> grants = await db
            .PermitGrants.Where(p =>
                p.BroadcasterId == broadcasterId
                && p.RevokedAt == null
                && p.DeletedAt == null
                && (p.ExpiresAt == null || p.ExpiresAt > now)
            )
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> usernames = await ResolveUsernamesAsync(grants, cancellationToken);
        Dictionary<Guid, string> actionKeys = await ResolveActionKeysAsync(
            grants,
            cancellationToken
        );

        List<PermitGrantDto> dtos =
        [
            .. grants.Select(g =>
                ToDto(
                    g,
                    usernames.GetValueOrDefault(g.UserId),
                    g.ActionDefinitionId is Guid id ? actionKeys.GetValueOrDefault(id) : null
                )
            ),
        ];
        return Result.Success<IReadOnlyList<PermitGrantDto>>(dtos);
    }

    public async Task<Result<int>> ExpireDueGrantsAsync(
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        // System sweep across every channel — bypass the tenant filter (and re-apply soft-delete manually).
        List<PermitGrant> due = await db
            .PermitGrants.IgnoreQueryFilters()
            .Where(p =>
                p.DeletedAt == null
                && p.RevokedAt == null
                && p.ExpiresAt != null
                && p.ExpiresAt <= now
            )
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
            return Result.Success(0);

        foreach (PermitGrant grant in due)
        {
            grant.RevokedAt = now;
            grant.Reason = "expired";
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (PermitGrant grant in due)
            await PublishRevokedAsync(grant, revokedByUserId: null, "expired", cancellationToken);
        return Result.Success(due.Count);
    }

    private async Task<List<PermitGrant>> SelectForRevocationAsync(
        List<PermitGrant> active,
        string? actionKeyOrRole,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(actionKeyOrRole))
            return active;

        if (Enum.TryParse(actionKeyOrRole, ignoreCase: true, out ManagementRole role))
            return active
                .Where(p => p.GrantType == PermitGrantType.Role && p.GrantedRole == role)
                .ToList();

        Guid? actionId = await db
            .ActionDefinitions.Where(a => a.ActionKey == actionKeyOrRole)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        return actionId is null
            ? []
            : active
                .Where(p =>
                    p.GrantType == PermitGrantType.Capability && p.ActionDefinitionId == actionId
                )
                .ToList();
    }

    private async Task<Dictionary<Guid, string>> ResolveUsernamesAsync(
        List<PermitGrant> grants,
        CancellationToken ct
    )
    {
        List<Guid> userIds = grants.Select(g => g.UserId).Distinct().ToList();
        return await db
            .Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    private async Task<Dictionary<Guid, string>> ResolveActionKeysAsync(
        List<PermitGrant> grants,
        CancellationToken ct
    )
    {
        List<Guid> actionIds = grants
            .Where(g => g.ActionDefinitionId != null)
            .Select(g => g.ActionDefinitionId!.Value)
            .Distinct()
            .ToList();
        return actionIds.Count == 0
            ? []
            : await db
                .ActionDefinitions.Where(a => actionIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.ActionKey, ct);
    }

    private async Task<string?> LookupUsernameAsync(Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct);

    private async Task PublishGrantedAsync(
        PermitGrant grant,
        string? capabilityActionKey,
        CancellationToken ct
    ) =>
        await eventBus.PublishAsync(
            new PermitGrantedEvent
            {
                BroadcasterId = grant.BroadcasterId,
                GrantId = grant.Id,
                TargetUserId = grant.UserId,
                GrantType = grant.GrantType,
                GrantedRole = grant.GrantedRole,
                CapabilityActionKey = capabilityActionKey,
                GrantedByUserId = grant.GrantedByUserId,
                ExpiresAt = grant.ExpiresAt,
            },
            ct
        );

    private async Task PublishRevokedAsync(
        PermitGrant grant,
        Guid? revokedByUserId,
        string reason,
        CancellationToken ct
    ) =>
        await eventBus.PublishAsync(
            new PermitRevokedEvent
            {
                BroadcasterId = grant.BroadcasterId,
                GrantId = grant.Id,
                TargetUserId = grant.UserId,
                RevokedByUserId = revokedByUserId,
                Reason = reason,
            },
            ct
        );

    private static PermitGrantDto ToDto(
        PermitGrant g,
        string? username,
        string? capabilityActionKey
    ) =>
        new(
            g.Id,
            g.UserId,
            username,
            g.GrantType,
            g.GrantedRole,
            capabilityActionKey,
            g.GrantedByUserId,
            g.ExpiresAt,
            g.RevokedAt,
            g.Reason,
            g.CreatedAt
        );
}
