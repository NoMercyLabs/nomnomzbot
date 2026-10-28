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
            query = query.Where(c =>
                c.Name.Contains(search) || c.TwitchChannelId!.Contains(search)
            );
        return query.OrderBy(c => c.Name).Skip((page - 1) * take).Take(take).ToListAsync(ct);
    }

    public Task<int> CountAsync(string? search, CancellationToken ct = default)
    {
        IQueryable<Channel> query = Set.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c =>
                c.Name.Contains(search) || c.TwitchChannelId!.Contains(search)
            );
        return query.CountAsync(ct);
    }

    public Task<Channel?> GetByOverlayTokenAsync(string token, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(c => c.OverlayToken == token, ct);
}
