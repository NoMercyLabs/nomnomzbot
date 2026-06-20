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
/// Plane-B membership writes + Twitch reconciliation (roles-permissions §3.4). Upserts recompute the ladder
/// <c>LevelValue</c> from the role; the no-escalation guard keeps a grantor from minting a role above their
/// own resolved level; Owner memberships are protected from removal; and sync reconciles only the
/// externally-sourced (badge / Helix-editor) rows, leaving owner/bot-grant rows alone.
/// </summary>
public sealed class MembershipService(
    IApplicationDbContext db,
    IRoleResolver roleResolver,
    IEventBus eventBus,
    TimeProvider clock
) : IMembershipService
{
    private static readonly MembershipSource[] SyncedSources =
    [
        MembershipSource.TwitchBadge,
        MembershipSource.HelixEditors,
    ];

    public async Task<Result<ChannelMembershipDto>> SetManagementRoleAsync(
        Guid broadcasterId,
        Guid userId,
        ManagementRole role,
        MembershipSource source,
        Guid? grantedByUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (grantedByUserId is Guid grantor)
        {
            Result<int> grantorLevel = await roleResolver.ResolveEffectiveLevelAsync(
                grantor,
                broadcasterId,
                cancellationToken
            );
            int level = grantorLevel.IsSuccess ? grantorLevel.Value : 0;
            if (level < role.ToLevel())
                return Result.Failure<ChannelMembershipDto>(
                    "Cannot grant a management role above your own level.",
                    "FORBIDDEN"
                );
        }

        ChannelMembership? existing = await FindAsync(broadcasterId, userId, cancellationToken);
        ManagementRole? oldRole = existing?.ManagementRole;
        if (existing is null)
        {
            existing = new ChannelMembership
            {
                BroadcasterId = broadcasterId,
                UserId = userId,
                ManagementRole = role,
                LevelValue = role.ToLevel(),
                Source = source,
                GrantedByUserId = grantedByUserId,
                GrantedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.ChannelMemberships.Add(existing);
        }
        else
        {
            existing.ManagementRole = role;
            existing.LevelValue = role.ToLevel();
            existing.Source = source;
            existing.GrantedByUserId = grantedByUserId;
        }
        await db.SaveChangesAsync(cancellationToken);

        await PublishRoleChangedAsync(
            broadcasterId,
            userId,
            oldRole,
            role,
            source,
            grantedByUserId,
            cancellationToken
        );

        string? username = await LookupUsernameAsync(userId, cancellationToken);
        return Result.Success(ToDto(existing, username));
    }

    public async Task<Result> RemoveManagementRoleAsync(
        Guid broadcasterId,
        Guid userId,
        Guid? removedByUserId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelMembership? existing = await FindAsync(broadcasterId, userId, cancellationToken);
        if (existing is null)
            return Result.Success();

        if (existing.Source == MembershipSource.Owner)
            return Result.Failure(
                "The channel owner's membership cannot be removed.",
                "VALIDATION_FAILED"
            );

        ManagementRole oldRole = existing.ManagementRole;
        existing.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        await PublishRoleChangedAsync(
            broadcasterId,
            userId,
            oldRole,
            null,
            existing.Source,
            removedByUserId,
            cancellationToken
        );
        return Result.Success();
    }

    public async Task<Result> SyncManagementFromTwitchAsync(
        Guid broadcasterId,
        IReadOnlyList<TwitchManagementMember> snapshot,
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<ChannelMembership> existingSynced = await db
            .ChannelMemberships.Where(m =>
                m.BroadcasterId == broadcasterId && SyncedSources.Contains(m.Source)
            )
            .ToListAsync(cancellationToken);
        Dictionary<Guid, ChannelMembership> byUser = existingSynced.ToDictionary(m => m.UserId);
        HashSet<Guid> snapshotUsers = snapshot.Select(m => m.UserId).ToHashSet();

        List<(
            Guid UserId,
            ManagementRole? Old,
            ManagementRole? New,
            MembershipSource Source
        )> deltas = [];

        foreach (TwitchManagementMember member in snapshot)
        {
            if (byUser.TryGetValue(member.UserId, out ChannelMembership? row))
            {
                ManagementRole? old = row.ManagementRole;
                row.LastSyncedAt = now;
                if (row.ManagementRole != member.Role || row.Source != member.Source)
                {
                    row.ManagementRole = member.Role;
                    row.LevelValue = member.Role.ToLevel();
                    row.Source = member.Source;
                    deltas.Add((member.UserId, old, member.Role, member.Source));
                }
            }
            else
            {
                db.ChannelMemberships.Add(
                    new ChannelMembership
                    {
                        BroadcasterId = broadcasterId,
                        UserId = member.UserId,
                        ManagementRole = member.Role,
                        LevelValue = member.Role.ToLevel(),
                        Source = member.Source,
                        GrantedAt = now,
                        LastSyncedAt = now,
                    }
                );
                deltas.Add((member.UserId, null, member.Role, member.Source));
            }
        }

        foreach (
            ChannelMembership stale in existingSynced.Where(m => !snapshotUsers.Contains(m.UserId))
        )
        {
            stale.DeletedAt = now;
            deltas.Add((stale.UserId, stale.ManagementRole, null, stale.Source));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (
            (
                Guid userId,
                ManagementRole? old,
                ManagementRole? @new,
                MembershipSource source
            ) in deltas
        )
            await PublishRoleChangedAsync(
                broadcasterId,
                userId,
                old,
                @new,
                source,
                null,
                cancellationToken
            );

        return Result.Success();
    }

    public async Task<Result<PagedList<ChannelMembershipDto>>> ListMembershipsAsync(
        Guid broadcasterId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<ChannelMembership> query = db.ChannelMemberships.Where(m =>
            m.BroadcasterId == broadcasterId
        );
        int total = await query.CountAsync(cancellationToken);
        List<ChannelMembership> rows = await query
            .OrderByDescending(m => m.LevelValue)
            .ThenBy(m => m.GrantedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<Guid> userIds = rows.Select(m => m.UserId).ToList();
        Dictionary<Guid, string> usernames = await db
            .Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);

        List<ChannelMembershipDto> dtos =
        [
            .. rows.Select(m =>
                ToDto(m, usernames.TryGetValue(m.UserId, out string? name) ? name : null)
            ),
        ];
        return Result.Success(new PagedList<ChannelMembershipDto>(dtos, page, pageSize, total));
    }

    private async Task<ChannelMembership?> FindAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken ct
    ) =>
        await db.ChannelMemberships.FirstOrDefaultAsync(
            m => m.BroadcasterId == broadcasterId && m.UserId == userId,
            ct
        );

    private async Task<string?> LookupUsernameAsync(Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct);

    private async Task PublishRoleChangedAsync(
        Guid broadcasterId,
        Guid userId,
        ManagementRole? oldRole,
        ManagementRole? newRole,
        MembershipSource source,
        Guid? changedByUserId,
        CancellationToken ct
    ) =>
        await eventBus.PublishAsync(
            new ManagementRoleChangedEvent
            {
                BroadcasterId = broadcasterId,
                TargetUserId = userId,
                OldRole = oldRole,
                NewRole = newRole,
                Source = source,
                ChangedByUserId = changedByUserId,
            },
            ct
        );

    private static ChannelMembershipDto ToDto(ChannelMembership m, string? username) =>
        new(
            m.Id,
            m.UserId,
            username,
            m.ManagementRole,
            m.LevelValue,
            m.Source,
            m.GrantedByUserId,
            m.GrantedAt,
            m.LastSyncedAt
        );
}
