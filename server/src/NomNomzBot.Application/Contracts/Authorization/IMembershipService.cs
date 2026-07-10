// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Plane-B channel-management membership writes + Twitch sync (roles-permissions §3.4). Owns who holds a
/// management role (Moderator / LeadModerator / Editor / Broadcaster) in a channel and at what ladder level.
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// Upserts a management-ladder membership (recomputes <c>LevelValue</c>). No-escalation guard:
    /// <paramref name="grantedByUserId"/> (when set) may not grant a role above their own resolved level
    /// (<c>FORBIDDEN</c>). Emits <c>ManagementRoleChangedEvent</c>.
    /// </summary>
    Task<Result<ChannelMembershipDto>> SetManagementRoleAsync(
        Guid broadcasterId,
        Guid userId,
        ManagementRole role,
        MembershipSource source,
        Guid? grantedByUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Soft-deletes a membership (demote/remove). An <c>Owner</c>-sourced membership is non-removable
    /// (<c>VALIDATION_FAILED</c>). Emits <c>ManagementRoleChangedEvent</c> with <c>NewRole = null</c>.
    /// </summary>
    Task<Result> RemoveManagementRoleAsync(
        Guid broadcasterId,
        Guid userId,
        Guid? removedByUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reconciles the <c>TwitchBadge</c>- + <c>HelixEditors</c>-sourced memberships from a freshly-fetched
    /// snapshot: idempotent upsert + prune of stale synced rows (BotGrant/Owner rows untouched), sets
    /// <c>LastSyncedAt</c>, and emits a <c>ManagementRoleChangedEvent</c> per delta.
    /// <para>
    /// Prune-safe: only rows whose <see cref="MembershipSource"/> is in <paramref name="authoritativeSources"/>
    /// (the sources whose Twitch read succeeded this run) are eligible for pruning — so a failed moderator- or
    /// editor-read never wipes that source's roles. A member present in the snapshot is upserted regardless.
    /// </para>
    /// </summary>
    Task<Result> SyncManagementFromTwitchAsync(
        Guid broadcasterId,
        IReadOnlyList<TwitchManagementMember> snapshot,
        IReadOnlySet<MembershipSource> authoritativeSources,
        CancellationToken cancellationToken = default
    );

    /// <summary>Paginated membership list for the dashboard roles screen (highest ladder level first).</summary>
    Task<Result<PagedList<ChannelMembershipDto>>> ListMembershipsAsync(
        Guid broadcasterId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default
    );
}
