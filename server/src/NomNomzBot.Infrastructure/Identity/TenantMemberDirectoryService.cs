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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads one tenant's people across the tenant query filter (the caller is a platform operator, never the
/// tenant itself), re-applying the soft-delete filter by hand on every set that has one.
/// </summary>
public sealed class TenantMemberDirectoryService(IApplicationDbContext db)
    : ITenantMemberDirectoryService
{
    private const string OwnerRelation = "owner";
    private const string ManagerRelation = "manager";
    private const string CommunityRelation = "community";
    private const string ViewerRelation = "viewer";

    public async Task<bool> IsMemberAsync(
        Guid channelId,
        Guid userId,
        CancellationToken ct = default
    ) => await MemberUserIds(channelId).AnyAsync(id => id == userId, ct);

    public async Task<PagedList<TenantMemberDto>> ListAsync(
        Guid channelId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Guid ownerId = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == channelId && c.DeletedAt == null)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        IQueryable<Guid> memberIds = MemberUserIds(channelId);
        IQueryable<User> people = db
            .Users.IgnoreQueryFilters()
            .Where(u => memberIds.Contains(u.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            people = people.Where(u =>
                u.UsernameNormalized.Contains(term) || u.DisplayName.ToLower().Contains(term)
            );
        }

        int total = await people.CountAsync(ct);
        List<PersonRow> page = await people
            .OrderBy(u => u.Id == ownerId ? 0 : 1)
            .ThenBy(u => u.UsernameNormalized)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new PersonRow(u.Id, u.Username, u.DisplayName, u.ProfileImageUrl))
            .ToListAsync(ct);

        List<Guid> pageIds = [.. page.Select(p => p.Id)];
        // Highest role / standing per person, folded in memory: the enums may be stored as text, where a SQL
        // MAX would rank them alphabetically instead of by rung.
        List<RoleRow> roleRows = await db
            .ChannelMemberships.IgnoreQueryFilters()
            .Where(m =>
                m.BroadcasterId == channelId && m.DeletedAt == null && pageIds.Contains(m.UserId)
            )
            .Select(m => new RoleRow(m.UserId, m.ManagementRole))
            .ToListAsync(ct);
        List<StandingRow> standingRows = await db
            .ChannelCommunityStandings.IgnoreQueryFilters()
            .Where(s => s.BroadcasterId == channelId && pageIds.Contains(s.UserId))
            .Select(s => new StandingRow(s.UserId, s.Standing))
            .ToListAsync(ct);
        Dictionary<Guid, ManagementRole> roles = roleRows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Role));
        Dictionary<Guid, CommunityStanding> standings = standingRows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Standing));

        List<TenantMemberDto> items =
        [
            .. page.Select(p => ToDto(p, p.Id == ownerId, roles, standings)),
        ];
        return new PagedList<TenantMemberDto>(items, pagination.Page, pagination.PageSize, total);
    }

    /// <summary>Every user id with a live tie to the channel: owner, membership, standing or viewer profile.</summary>
    private IQueryable<Guid> MemberUserIds(Guid channelId) =>
        db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == channelId && c.DeletedAt == null)
            .Select(c => c.OwnerUserId)
            .Union(
                db.ChannelMemberships.IgnoreQueryFilters()
                    .Where(m => m.BroadcasterId == channelId && m.DeletedAt == null)
                    .Select(m => m.UserId)
            )
            .Union(
                db.ChannelCommunityStandings.IgnoreQueryFilters()
                    .Where(s => s.BroadcasterId == channelId)
                    .Select(s => s.UserId)
            )
            .Union(
                db.ViewerProfiles.IgnoreQueryFilters()
                    .Where(v => v.BroadcasterId == channelId && v.DeletedAt == null)
                    .Select(v => v.ViewerUserId)
            );

    private static TenantMemberDto ToDto(
        PersonRow person,
        bool isOwner,
        Dictionary<Guid, ManagementRole> roles,
        Dictionary<Guid, CommunityStanding> standings
    )
    {
        ManagementRole? role = roles.TryGetValue(person.Id, out ManagementRole r) ? r : null;
        CommunityStanding? standing = standings.TryGetValue(person.Id, out CommunityStanding s)
            ? s
            : null;
        string relation =
            isOwner ? OwnerRelation
            : role is not null ? ManagerRelation
            : standing is not null and not CommunityStanding.Everyone ? CommunityRelation
            : ViewerRelation;

        return new TenantMemberDto(
            person.Id,
            person.Username,
            person.DisplayName,
            person.ProfileImageUrl,
            relation,
            isOwner ? nameof(ManagementRole.Broadcaster) : role?.ToString(),
            standing?.ToString()
        );
    }

    private sealed record PersonRow(
        Guid Id,
        string Username,
        string DisplayName,
        string? ProfileImageUrl
    );

    private sealed record RoleRow(Guid UserId, ManagementRole Role);

    private sealed record StandingRow(Guid UserId, CommunityStanding Standing);
}
