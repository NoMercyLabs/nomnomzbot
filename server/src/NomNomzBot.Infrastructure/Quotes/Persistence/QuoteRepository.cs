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
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;

namespace NomNomzBot.Infrastructure.Quotes.Persistence;

public class QuoteRepository : GenericRepository<Quote>
{
    public QuoteRepository(AppDbContext db)
        : base(db) { }

    public Task<Quote?> GetByNumberAsync(
        Guid broadcasterId,
        int number,
        CancellationToken ct = default
    ) => Set.FirstOrDefaultAsync(q => q.BroadcasterId == broadcasterId && q.Number == number, ct);
}
