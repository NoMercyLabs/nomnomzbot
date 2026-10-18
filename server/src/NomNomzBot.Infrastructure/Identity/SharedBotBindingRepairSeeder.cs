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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Repairs channels the old connect flow bound to the system bot account as their "own" bot. The flow found
/// the shared <c>BotAccount</c> by Twitch user id and wrote a <c>ChannelBotAuthorization</c> to it, so the
/// channel read as having a white-label bot that was really the shared one. The repair ends every active
/// authorization whose account is a shared (system) bot, which turns the channel back into "uses the shared
/// bot". It touches only the authorization row: the system account and its token connection stay exactly as
/// they are, because other channels speak through them. Idempotent: an ended row no longer matches. Plain EF,
/// so it runs the same on SQLite and Postgres and needs no migration.
/// </summary>
public sealed class SharedBotBindingRepairSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SharedBotBindingRepairSeeder> _logger;

    public SharedBotBindingRepairSeeder(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        ILogger<SharedBotBindingRepairSeeder> logger
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // Late, like the other repairs: it only reads and ends rows that already exist.
    public int Order => 945;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<ChannelBotAuthorization> bound = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .Where(a => a.IsActive && a.DeletedAt == null)
            .Join(
                _db.BotAccounts.IgnoreQueryFilters()
                    .Where(b => b.IdentityType == AuthEnums.BotIdentityType.Shared),
                a => a.BotAccountId,
                b => b.Id,
                (a, _) => a
            )
            .ToListAsync(ct);
        if (bound.Count == 0)
            return;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (ChannelBotAuthorization authorization in bound)
        {
            authorization.IsActive = false;
            authorization.DeletedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Ended {Count} channel binding(s) to the system bot; those channels now use the shared bot",
            bound.Count
        );
    }
}
