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

public class UserRepository : GenericRepository<User>
{
    public UserRepository(AppDbContext db)
        : base(db) { }

    public Task<User?> GetByIdAsync(string userId, CancellationToken ct = default) =>
        Guid.TryParse(userId, out Guid id)
            ? Set.Include(u => u.Pronoun).FirstOrDefaultAsync(u => u.Id == id, ct)
            : Task.FromResult<User?>(null);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(u => u.Username == username, ct);

    public Task<List<User>> SearchAsync(
        string query,
        int take = 10,
        CancellationToken ct = default
    ) =>
        Set.Where(u => u.Username.Contains(query) || u.DisplayName.Contains(query))
            .Take(take)
            .ToListAsync(ct);
}
