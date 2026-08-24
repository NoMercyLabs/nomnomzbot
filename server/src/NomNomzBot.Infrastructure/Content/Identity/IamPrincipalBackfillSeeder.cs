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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// D2 bootstrap gap backfill. <c>AuthService.MintPlatformOwnerPrincipalAsync</c> (via
/// <see cref="IPlatformOwnerPrincipalMinter"/>) only mints a real <c>IamPrincipal</c> + owner role
/// assignment at PROMOTION time. Any account whose <c>User.IsPlatformPrincipal</c> was already <c>true</c>
/// before that fix landed keeps the marker with no <c>IamPrincipal</c> row, and
/// <c>IamCallerPrincipalResolverService</c> correctly refuses to fabricate one — locking that account out
/// of every IAM-gated action ("No IAM principal is registered for this caller."). This seeder finds every
/// such orphaned platform principal on every startup and mints it via the same
/// <see cref="IPlatformOwnerPrincipalMinter"/> path <c>AuthService</c> uses — never a duplicated
/// row-builder. Runs after <see cref="IamCatalogSeeder"/> (Order 6), whose <c>platform-super-admin</c> role
/// row this depends on. Idempotent: a user who already has a principal is never touched again.
/// </summary>
public sealed class IamPrincipalBackfillSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly IPlatformOwnerPrincipalMinter _minter;
    private readonly ILogger<IamPrincipalBackfillSeeder> _logger;

    public IamPrincipalBackfillSeeder(
        IApplicationDbContext db,
        IPlatformOwnerPrincipalMinter minter,
        ILogger<IamPrincipalBackfillSeeder> logger
    )
    {
        _db = db;
        _minter = minter;
        _logger = logger;
    }

    public int Order => 7;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<User> orphaned = await _db
            .Users.Where(u => u.IsPlatformPrincipal)
            .Where(u => !_db.IamPrincipals.Any(p => p.UserId == u.Id))
            .ToListAsync(ct);

        if (orphaned.Count == 0)
            return;

        foreach (User user in orphaned)
        {
            await _minter.MintAsync(user.Id, ct);

            // Flush after each mint (rather than once at the end) so the change tracker — and thus the
            // "does system-bootstrap already exist" / "does this user already have a principal" checks
            // inside the minter — see every prior iteration's rows. Without this, minting N orphaned
            // users in one pass would create N duplicate system-bootstrap service-account principals,
            // since none of them are visible to the DB query until saved.
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Backfilled a missing IamPrincipal for pre-existing platform principal {UserId} — "
                    + "promoted before the D2 bootstrap fix started minting one at promotion time.",
                user.Id
            );
        }
    }
}
