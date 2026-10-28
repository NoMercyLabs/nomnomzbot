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
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Rewards.Persistence;

public class RewardRepository : GenericRepository<Reward>
{
    public RewardRepository(AppDbContext db)
        : base(db) { }

    public Task<List<Reward>> GetByBroadcasterIdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Set.Where(r => r.BroadcasterId == broadcasterId).OrderBy(r => r.Title).ToListAsync(ct);

    public Task<Reward?> GetByIdAndBroadcasterAsync(
        Guid id,
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Set.FirstOrDefaultAsync(r => r.Id == id && r.BroadcasterId == broadcasterId, ct);

    public Task<List<Reward>> GetPagedAsync(
        Guid broadcasterId,
        int page,
        int take,
        CancellationToken ct = default
    ) =>
        Set.Where(r => r.BroadcasterId == broadcasterId)
            .OrderBy(r => r.Title)
            .Skip((page - 1) * take)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> GetCountAsync(Guid broadcasterId, CancellationToken ct = default) =>
        Set.CountAsync(r => r.BroadcasterId == broadcasterId, ct);
}
