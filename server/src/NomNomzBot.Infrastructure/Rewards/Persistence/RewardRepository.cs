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
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Rewards.Persistence;

public class RewardRepository : GenericRepository<Reward>
{
    public RewardRepository(AppDbContext db)
        : base(db) { }

    public Task<List<Reward>> GetByBroadcasterIdAsync(
        string broadcasterId,
        CancellationToken ct = default
    ) => Set.Where(r => r.BroadcasterId == broadcasterId).OrderBy(r => r.Title).ToListAsync(ct);

    public Task<Reward?> GetByIdAndBroadcasterAsync(
        Guid id,
        string broadcasterId,
        CancellationToken ct = default
    ) => Set.FirstOrDefaultAsync(r => r.Id == id && r.BroadcasterId == broadcasterId, ct);

    public Task<List<Reward>> GetPagedAsync(
        string broadcasterId,
        int page,
        int take,
        CancellationToken ct = default
    ) =>
        Set.Where(r => r.BroadcasterId == broadcasterId)
            .OrderBy(r => r.Title)
            .Skip((page - 1) * take)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> GetCountAsync(string broadcasterId, CancellationToken ct = default) =>
        Set.CountAsync(r => r.BroadcasterId == broadcasterId, ct);
}
