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
