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

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Resolves the effective authorization level (roles-permissions §3.2) by reading the channel's
/// community-standing, management-membership, and active permit rows and taking the <c>MAX</c> on the unified
/// ladder. The <c>!permit</c> filter excludes revoked, soft-deleted, and expired grants. Pure read.
/// </summary>
public sealed class RoleResolver(IApplicationDbContext db, TimeProvider clock) : IRoleResolver
{
    private const int BroadcasterLevel = 40;

    public async Task<Result<int>> ResolveEffectiveLevelAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        AccessFacts facts = await LoadFactsAsync(userId, broadcasterId, cancellationToken);
        return Result.Success(facts.EffectiveLevel);
    }

    public async Task<Result<ResolvedAccessDto>> ResolveAccessAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        AccessFacts facts = await LoadFactsAsync(userId, broadcasterId, cancellationToken);
        return Result.Success(
            new ResolvedAccessDto(
                userId,
                broadcasterId,
                facts.EffectiveLevel,
                facts.Standing,
                facts.CommunityLevel,
                facts.Role,
                facts.ManagementLevel,
                facts.PermitRole,
                facts.PermitCapabilities,
                facts.WinningSource
            )
        );
    }

    public async Task<Result<bool>> HasCapabilityAsync(
        Guid userId,
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    )
    {
        ActionDefinition? action = await db
            .ActionDefinitions.Where(a => a.ActionKey == actionKey)
            .FirstOrDefaultAsync(cancellationToken);

        // Unknown action keys fail closed.
        if (action is null)
            return Result.Success(false);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        // A direct capability grant for exactly this action is sufficient.
        bool directGrant = await db.PermitGrants.AnyAsync(
            p =>
                p.BroadcasterId == broadcasterId
                && p.UserId == userId
                && p.GrantType == PermitGrantType.Capability
                && p.ActionDefinitionId == action.Id
                && p.RevokedAt == null
                && p.DeletedAt == null
                && (p.ExpiresAt == null || p.ExpiresAt > now),
            cancellationToken
        );
        if (directGrant)
            return Result.Success(true);

        // Otherwise the resolved level must meet the action's effective required level.
        int required = await EffectiveActionLevelAsync(broadcasterId, action, cancellationToken);
        AccessFacts facts = await LoadFactsAsync(userId, broadcasterId, cancellationToken);
        return Result.Success(facts.EffectiveLevel >= required);
    }

    /// <summary>Loads the three plane contributions and folds them into the effective level + winning source.</summary>
    private async Task<AccessFacts> LoadFactsAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        ChannelCommunityStanding? standing = await db
            .ChannelCommunityStandings.Where(s =>
                s.BroadcasterId == broadcasterId && s.UserId == userId
            )
            .FirstOrDefaultAsync(ct);

        ChannelMembership? membership = await db
            .ChannelMemberships.Where(m => m.BroadcasterId == broadcasterId && m.UserId == userId)
            .FirstOrDefaultAsync(ct);

        List<PermitGrant> activePermits = await db
            .PermitGrants.Where(p =>
                p.BroadcasterId == broadcasterId
                && p.UserId == userId
                && p.RevokedAt == null
                && p.DeletedAt == null
                && (p.ExpiresAt == null || p.ExpiresAt > now)
            )
            .ToListAsync(ct);

        List<Guid> capabilityActionIds =
        [
            .. activePermits
                .Where(p =>
                    p.GrantType == PermitGrantType.Capability && p.ActionDefinitionId != null
                )
                .Select(p => p.ActionDefinitionId!.Value),
        ];
        List<string> capabilities =
            capabilityActionIds.Count == 0
                ? []
                : await db
                    .ActionDefinitions.Where(a => capabilityActionIds.Contains(a.Id))
                    .Select(a => a.ActionKey)
                    .ToListAsync(ct);

        int communityLevel = standing?.LevelValue ?? 0;
        int managementLevel = membership?.LevelValue ?? 0;

        ManagementRole? permitRole = activePermits
            .Where(p => p.GrantType == PermitGrantType.Role && p.GrantedRole != null)
            .Select(p => (ManagementRole?)p.GrantedRole!.Value)
            .OrderByDescending(r => r!.Value.ToLevel())
            .FirstOrDefault();
        int permitRoleLevel = permitRole?.ToLevel() ?? 0;

        int effective = Math.Max(communityLevel, Math.Max(managementLevel, permitRoleLevel));
        string winning =
            effective == 0 ? "community"
            : permitRoleLevel == effective ? "permit"
            : managementLevel == effective ? "management"
            : "community";

        return new AccessFacts(
            standing?.Standing ?? CommunityStanding.Everyone,
            communityLevel,
            membership?.ManagementRole,
            managementLevel,
            permitRole,
            capabilities,
            effective,
            winning
        );
    }

    /// <summary>The action's effective required level for a channel: <c>clamp(override ?? default, floor, Broadcaster)</c>.</summary>
    private async Task<int> EffectiveActionLevelAsync(
        Guid broadcasterId,
        ActionDefinition action,
        CancellationToken ct
    )
    {
        int? overrideLevel = await db
            .ChannelActionOverrides.Where(o =>
                o.BroadcasterId == broadcasterId
                && o.ActionDefinitionId == action.Id
                && o.DeletedAt == null
            )
            .Select(o => (int?)o.OverrideLevel)
            .FirstOrDefaultAsync(ct);

        int desired = overrideLevel ?? action.DefaultLevel;
        return Math.Clamp(desired, action.FloorLevel, BroadcasterLevel);
    }

    private sealed record AccessFacts(
        CommunityStanding Standing,
        int CommunityLevel,
        ManagementRole? Role,
        int ManagementLevel,
        ManagementRole? PermitRole,
        IReadOnlyList<string> PermitCapabilities,
        int EffectiveLevel,
        string WinningSource
    );
}
