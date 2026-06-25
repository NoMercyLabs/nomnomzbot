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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Identity.Persistence;

public class ChannelRepository : GenericRepository<Channel>
{
    public ChannelRepository(AppDbContext db)
        : base(db) { }

    public Task<Channel?> GetByBroadcasterIdAsync(
        string broadcasterId,
        CancellationToken ct = default
    ) =>
        Guid.TryParse(broadcasterId, out Guid id)
            ? Set.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == id, ct)
            : Task.FromResult<Channel?>(null);

    public Task<List<Channel>> GetEnabledChannelsAsync(CancellationToken ct = default) =>
        Set.Where(c => c.Enabled && c.IsOnboarded).ToListAsync(ct);

    public Task<List<Channel>> GetPagedAsync(
        int page,
        int take,
        string? search,
        CancellationToken ct = default
    )
    {
        IQueryable<Channel> query = Set.Include(c => c.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.TwitchChannelId.Contains(search));
        return query.OrderBy(c => c.Name).Skip((page - 1) * take).Take(take).ToListAsync(ct);
    }

    public Task<int> CountAsync(string? search, CancellationToken ct = default)
    {
        IQueryable<Channel> query = Set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.TwitchChannelId.Contains(search));
        return query.CountAsync(ct);
    }

    public Task<Channel?> GetByOverlayTokenAsync(string token, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(c => c.OverlayToken == token, ct);
}
