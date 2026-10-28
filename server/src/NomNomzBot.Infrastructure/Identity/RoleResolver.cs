// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Resolves the effective authorization level (roles-permissions §3.2) by reading the channel's
/// community-standing, management-membership, and active permit rows and taking the <c>MAX</c> on the unified
/// ladder. The <c>!permit</c> filter excludes revoked, soft-deleted, and expired grants. Pure read: while a
/// request acts as someone, a membership their own login would have written comes from
/// <see cref="IActAsMembershipOverlay"/> instead of a row.
/// </summary>
public sealed class RoleResolver(
    IApplicationDbContext db,
    TimeProvider clock,
    IActAsMembershipOverlay actAsOverlay,
    IViewAsScope viewAs
) : IRoleResolver
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
                PermissionLevelNames.ToName(facts.EffectiveLevel),
                facts.Standing.ToString(),
                PermissionLevelNames.ToName(facts.CommunityLevel),
                facts.Role?.ToString(),
                PermissionLevelNames.ToName(facts.ManagementLevel),
                facts.PermitRole?.ToString(),
                facts.PermitCapabilities,
                facts.WinningSource,
                heldActionKeys
            )
        );
    }

    public async Task<Result<bool>> IsNetworkBlockedAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        // The network-wide block (S-ADMIN-8b) is a single, NON-tenant-filtered flag, so it denies every
        // Gate-2 action in EVERY tenant for as long as it is active/partial, including a tenant that never
        // carried a per-tenant deny row for this actor at all.
        bool networkBlocked = await db.NetworkBlocks.AnyAsync(
            b => b.TargetUserId == userId && b.Status != NetworkBlockStatus.Lifted,
            cancellationToken
        );
        return Result.Success(networkBlocked);
    }

    public async Task<Result<bool>> HasCapabilityAsync(
        Guid userId,
        Guid broadcasterId,
        string actionKey,
        CancellationToken cancellationToken = default
    )
    {
        Result<bool> networkBlocked = await IsNetworkBlockedAsync(userId, cancellationToken);
        if (networkBlocked is { IsSuccess: true, Value: true })
            return Result.Success(false);

        ActionDefinition? action = await db
            .ActionDefinitions.Where(a => a.ActionKey == actionKey)
            .FirstOrDefaultAsync(cancellationToken);

        // Unknown action keys fail closed.
        if (action is null)
            return Result.Success(false);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        // A direct capability grant for exactly this action, or for the Bot manager bundle that covers it, is
        // sufficient, except while viewing as a lower role.
        List<Guid?> grantedActionIds =
        [
            action.Id,
            .. await BundleActionIdsAsync(actionKey, cancellationToken),
        ];
        bool directGrant =
            viewAs.CeilingLevel is null
            && await db.PermitGrants.AnyAsync(
                p =>
                    p.BroadcasterId == broadcasterId
                    && p.UserId == userId
                    && p.GrantType == PermitGrantType.Capability
                    && grantedActionIds.Contains(p.ActionDefinitionId)
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

    /// <summary>The id of the Bot manager bundle key when the bundle covers <paramref name="actionKey"/>, else none.</summary>
    private async Task<List<Guid?>> BundleActionIdsAsync(string actionKey, CancellationToken ct)
    {
        if (!BotManagerBundle.Covers(actionKey))
            return [];

        Guid bundleId = await db
            .ActionDefinitions.Where(a => a.ActionKey == BotManagerBundle.Key)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);
        return bundleId == Guid.Empty ? [] : [bundleId];
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
                    p is { GrantType: PermitGrantType.Capability, ActionDefinitionId: not null }
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

        // Acting as someone: the membership their own login would hold here, computed, never written.
        ManagementRole? actingRole =
            membership is null && !isChannelOwner
                ? await actAsOverlay.ResolveAsync(userId, broadcasterId, ct)
                : null;

        int communityLevel = standing?.LevelValue ?? 0;
        // The channel owner IS the Broadcaster on their own channel — no membership row needed (schema A.2:
        // one channel per owner). This is what lets a fresh self-host streamer use their own dashboard out of
        // the box, instead of being a role-less user on the channel they own.
        // Broadcaster is never a grantable role. A stored Broadcaster row on someone who is not the owner (written
        // before the grant was refused) counts for no more than the highest grantable role, Editor, until changed.
        bool staleBroadcasterRow =
            membership?.ManagementRole == ManagementRole.Broadcaster && !isChannelOwner;
        ManagementRole? storedRole = staleBroadcasterRow
            ? ManagementRole.Editor
            : membership?.ManagementRole;
        int storedLevel = staleBroadcasterRow
            ? ManagementRole.Editor.ToLevel()
            : membership?.LevelValue ?? 0;
        ManagementRole? managementRole =
            storedRole ?? actingRole ?? (isChannelOwner ? ManagementRole.Broadcaster : null);
        int managementLevel = Math.Max(
            membership is null ? actingRole?.ToLevel() ?? 0 : storedLevel,
            isChannelOwner ? BroadcasterLevel : 0
        );

        ManagementRole? permitRole = activePermits
            .Where(p => p is { GrantType: PermitGrantType.Role, GrantedRole: not null })
            .Select(p => (ManagementRole?)p.GrantedRole!.Value)
            .OrderByDescending(r => r!.Value.ToLevel())
            .FirstOrDefault();
        int permitRoleLevel = permitRole?.ToLevel() ?? 0;

        int realLevel = Math.Max(communityLevel, Math.Max(managementLevel, permitRoleLevel));
        // View as (role-login-scopes.md): the ceiling only ever lowers the level, and drops direct grants.
        int? ceiling = viewAs.CeilingLevel;
        int effective = ceiling is { } cap ? Math.Min(realLevel, cap) : realLevel;
        if (ceiling is not null)
            capabilities = [];
        string winning =
            effective == 0 ? "community"
            : permitRoleLevel == effective ? "permit"
            : managementLevel == effective ? "management"
            : "community";

        return new(
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
        bool holdsBundle = directGrants.Contains(BotManagerBundle.Key);

        List<string> held = [];
        foreach (ActionDefinition action in actions)
        {
            int? overrideLevel = overrides.TryGetValue(action.Id, out int level) ? level : null;
            int required = ActionLevelPolicy.EffectiveRequiredLevel(action, overrideLevel);
            bool viaBundle = holdsBundle && BotManagerBundle.Covers(action.ActionKey);
            if (
                facts.EffectiveLevel >= required
                || directGrants.Contains(action.ActionKey)
                || viaBundle
            )
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
