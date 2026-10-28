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
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Widgets.Persistence;

public class WidgetRepository : GenericRepository<Widget>
{
    public WidgetRepository(AppDbContext db)
        : base(db) { }

    public Task<List<Widget>> GetByBroadcasterIdAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Set.Where(w => w.BroadcasterId == broadcasterId).OrderBy(w => w.Name).ToListAsync(ct);

    public Task<Widget?> GetByIdAndBroadcasterAsync(
        Guid id,
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Set.FirstOrDefaultAsync(w => w.Id == id && w.BroadcasterId == broadcasterId, ct);

    public Task<List<Widget>> GetPagedAsync(
        Guid broadcasterId,
        int page,
        int take,
        CancellationToken ct = default
    ) =>
        Set.Where(w => w.BroadcasterId == broadcasterId)
            .OrderBy(w => w.Name)
            .Skip((page - 1) * take)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> GetCountAsync(Guid broadcasterId, CancellationToken ct = default) =>
        Set.CountAsync(w => w.BroadcasterId == broadcasterId, ct);
}
