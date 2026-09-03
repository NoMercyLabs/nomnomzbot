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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Trust.Services;
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Infrastructure.Trust;

/// <summary>
/// Reads the channel's <see cref="TrustPolicy"/>, falling back to
/// <see cref="TrustScoreCalculator.DefaultPolicy"/> when the channel has never tuned anything.
/// </summary>
public sealed class TrustPolicyService : ITrustPolicyService
{
    private readonly IApplicationDbContext _db;

    public TrustPolicyService(IApplicationDbContext db) => _db = db;

    public async Task<TrustPolicy> GetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Cross-tenant-safe: scoring can run outside a resolved-tenant request (EventSub handlers,
        // background projections), so the broadcaster is matched explicitly rather than relying on the
        // ambient query filter. AsNoTracking because callers only read the values.
        TrustPolicy? stored = await _db
            .TrustPolicies.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.DeletedAt == null,
                cancellationToken
            );

        return stored ?? TrustScoreCalculator.DefaultPolicy;
    }
}
