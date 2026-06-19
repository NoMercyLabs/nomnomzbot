// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

/// <summary>
/// The single content-seed entry point (backend-structure §5.1). Discovers every
/// <see cref="ISeeder"/> (registered by the §4 assembly scan), sorts them by
/// <see cref="ISeeder.Order"/> ascending, and runs them sequentially inside ONE
/// <see cref="IUnitOfWork"/> transaction — all-or-nothing, rolled back on any failure.
/// Every seeder is idempotent (upsert by natural key), so this is safe to fire on every
/// startup: a re-run writes no duplicates and throws nothing.
/// </summary>
public sealed class SeedRunner
{
    private readonly IEnumerable<ISeeder> _seeders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SeedRunner> _logger;

    public SeedRunner(
        IEnumerable<ISeeder> seeders,
        IUnitOfWork unitOfWork,
        ILogger<SeedRunner> logger
    )
    {
        _seeders = seeders;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Stable ascending Order; ties preserve discovery order (OrderBy is a stable sort).
        IReadOnlyList<ISeeder> ordered = _seeders.OrderBy(s => s.Order).ToList();

        if (ordered.Count == 0)
        {
            _logger.LogInformation("No content seeders discovered — nothing to seed.");
            return;
        }

        _logger.LogInformation("Seeding content: {Count} seeder(s) in one transaction...", ordered.Count);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (ISeeder seeder in ordered)
            {
                await seeder.SeedAsync(ct);
            }

            // Single commit for all seeders' tracked changes — all-or-nothing.
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            _logger.LogInformation("Content seed complete ({Count} seeder(s)).", ordered.Count);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            _logger.LogError(ex, "Content seed failed — transaction rolled back, no rows written.");
            throw;
        }
    }
}
