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
        string id,
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
