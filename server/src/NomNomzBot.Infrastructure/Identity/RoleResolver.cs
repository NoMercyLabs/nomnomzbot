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
        IReadOnlyList<string> heldActionKeys = await ComputeHeldActionKeysAsync(
            broadcasterId,
            facts,
            cancellationToken
        );
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
                facts.WinningSource,
                heldActionKeys
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

        bool isChannelOwner = await db.Channels.AnyAsync(
            c => c.Id == broadcasterId && c.OwnerUserId == userId,
            ct
        );

        int communityLevel = standing?.LevelValue ?? 0;
        // The channel owner IS the Broadcaster on their own channel — no membership row needed (schema A.2:
        // one channel per owner). This is what lets a fresh self-host streamer use their own dashboard out of
        // the box, instead of being a role-less user on the channel they own.
        ManagementRole? managementRole =
            membership?.ManagementRole ?? (isChannelOwner ? ManagementRole.Broadcaster : null);
        int managementLevel = Math.Max(
            membership?.LevelValue ?? 0,
            isChannelOwner ? BroadcasterLevel : 0
        );

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
            managementRole,
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

        return ActionLevelPolicy.EffectiveRequiredLevel(action, overrideLevel);
    }

    /// <summary>
    /// Every action key in the catalogue this caller CLEARS on this channel — the same allow rule as
    /// <see cref="HasCapabilityAsync"/>, evaluated across the whole catalogue: their <see cref="AccessFacts.EffectiveLevel"/>
    /// meets the action's channel-effective required level (FOLDING IN the broadcaster's override), OR they hold
    /// a direct per-user capability grant for it. Loads the catalogue + the channel's overrides ONCE, then a
    /// MAX/compare per action in memory — no per-action round-trip.
    /// </summary>
    private async Task<IReadOnlyList<string>> ComputeHeldActionKeysAsync(
        Guid broadcasterId,
        AccessFacts facts,
        CancellationToken ct
    )
    {
        List<ActionDefinition> actions = await db.ActionDefinitions.ToListAsync(ct);
        Dictionary<Guid, int> overrides = await db
            .ChannelActionOverrides.Where(o =>
                o.BroadcasterId == broadcasterId && o.DeletedAt == null
            )
            .ToDictionaryAsync(o => o.ActionDefinitionId, o => o.OverrideLevel, ct);
        HashSet<string> directGrants = new(facts.PermitCapabilities, StringComparer.Ordinal);

        List<string> held = [];
        foreach (ActionDefinition action in actions)
        {
            int? overrideLevel = overrides.TryGetValue(action.Id, out int level)
                ? level
                : (int?)null;
            int required = ActionLevelPolicy.EffectiveRequiredLevel(action, overrideLevel);
            if (facts.EffectiveLevel >= required || directGrants.Contains(action.ActionKey))
                held.Add(action.ActionKey);
        }
        held.Sort(StringComparer.Ordinal);
        return held;
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
