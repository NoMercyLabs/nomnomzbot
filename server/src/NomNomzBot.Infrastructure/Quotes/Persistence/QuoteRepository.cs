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
