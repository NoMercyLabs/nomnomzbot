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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Default <see cref="IChannelAccessService"/>. Gate 1 is pure entry (roles-permissions.md §0: "entry ≠
/// permission") — any authenticated caller may resolve tenant context for a channel that exists, community
/// participant or channel manager alike. Per-action authorization (both the management-plane floors like
/// Moderator/Editor/Broadcaster AND the community-plane floors like Everyone) is Gate 2's job
/// (<c>ActionAuthorizationHandler</c> / <c>[RequireAction]</c>), not this gate's. Fails closed only on a
/// malformed id or a channel that does not exist.
/// </summary>
public sealed class ChannelAccessService : IChannelAccessService
{
    private readonly IApplicationDbContext _db;

    public ChannelAccessService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanResolveTenantAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        // userId / channelId are the internal user / tenant Guids in string form (JWT sub + tenant key).
        // userGuid is validated (an authenticated caller must carry a real user id) but, per Gate 1's pure-entry
        // role, is not otherwise checked against the channel here — that comparison is Gate 2's.
        if (!Guid.TryParse(userId, out Guid _) || !Guid.TryParse(channelId, out Guid channelGuid))
            return false;

        // The channel must exist (soft-deleted channels are excluded by the global query filter) AND be an
        // ACTIVE tenant — a suspended / platform-banned tenant (stream-admin.md §3.2 SuspendTenantAsync) is
        // refused at the gate, taking its whole channel-scoped API surface dark until reinstated. Admin
        // routes are not channel routes, so operators can still reinstate. Previously
        // this method also required the caller to be the owner, an active moderator, a management member, or a
        // platform principal — which fail-closed 403'd every community-plane participant (viewers, subs, VIPs)
        // before Gate 2 ever ran, since Gate 1 gates ALL explicit-channel-id requests regardless of the eventual
        // action's plane. Community-plane actions floor at Everyone(0) specifically so any authenticated
        // participant can reach them; management actions remain protected because Gate 2 still requires the
        // caller's resolved level (IRoleResolver — MAX of community standing / management membership / permit
        // grants, defaulting to 0 for an unrelated user) to meet that action's floor.
        return await _db.Channels.AnyAsync(
            c => c.Id == channelGuid && c.Status == AuthEnums.ChannelStatus.Active,
            cancellationToken
        );
    }

    public async Task<Guid> ResolveOwnChannelAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Guid.Empty;

        return await _db
            .Channels.Where(c => c.OwnerUserId == userGuid)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDashboardUserIdsAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        // Runs from an event handler with no tenant set: the tenant filter would hide every row, so the channel
        // is named explicitly and only soft-deleted rows stay out.
        List<Guid> owners = await _db
            .Channels.Where(c => c.Id == channelId)
            .Select(c => c.OwnerUserId)
            .ToListAsync(cancellationToken);
        List<Guid> moderators = await _db
            .ChannelModerators.IgnoreQueryFilters()
            .Where(m => m.ChannelId == channelId && m.DeletedAt == null)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        List<Guid> members = await _db
            .ChannelMemberships.IgnoreQueryFilters()
            .Where(m => m.BroadcasterId == channelId && m.DeletedAt == null)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        return
        [
            .. owners.Concat(moderators).Concat(members).Distinct().Select(id => id.ToString()),
        ];
    }
}
