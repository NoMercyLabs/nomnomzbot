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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Commands.Persistence;

public class CommandRepository : GenericRepository<Command>
{
    public CommandRepository(AppDbContext db)
        : base(db) { }

    public Task<List<Command>> GetByBroadcasterIdAsync(
        Guid broadcasterId,
        bool? enabled = null,
        CancellationToken ct = default
    )
    {
        IQueryable<Command> q = Set.Where(c => c.BroadcasterId == broadcasterId);
        if (enabled.HasValue)
            q = q.Where(c => c.IsEnabled == enabled.Value);
        return q.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<Command?> GetByNameAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    ) => Set.FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId && c.Name == name, ct);

    public Task<bool> ExistsByNameAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    ) => Set.AnyAsync(c => c.BroadcasterId == broadcasterId && c.Name == name, ct);

    public Task<List<Command>> SearchAsync(
        Guid broadcasterId,
        string search,
        CancellationToken ct = default
    ) =>
        Set.Where(c => c.BroadcasterId == broadcasterId && c.Name.Contains(search)).ToListAsync(ct);

    public Task<int> GetPagedCountAsync(
        Guid broadcasterId,
        string? search,
        CancellationToken ct = default
    )
    {
        IQueryable<Command> q = Set.Where(c => c.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(c => c.Name.Contains(search));
        return q.CountAsync(ct);
    }

    public Task<List<Command>> GetPagedAsync(
        Guid broadcasterId,
        int page,
        int take,
        string? search,
        CancellationToken ct = default
    )
    {
        IQueryable<Command> q = Set.Where(c => c.BroadcasterId == broadcasterId);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(c => c.Name.Contains(search));
        return q.OrderBy(c => c.Name).Skip((page - 1) * take).Take(take).ToListAsync(ct);
    }
}
