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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Backfills the platform-identity table (platform-identity §8.1). Every existing <see cref="User"/> predates
/// the <see cref="UserIdentity"/> row, so on boot this mints their primary <c>twitch</c> identity from the
/// user's own profile columns. Runs late (Order 900 — it reads <c>Users</c>, which are not seeded but arrive
/// via login/chat). Idempotent: an anti-join inserts only for users that lack a twitch identity, so a re-run
/// after the first backfill writes nothing.
/// </summary>
public sealed class TwitchIdentityBackfillSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public TwitchIdentityBackfillSeeder(IApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public int Order => 900;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Users with no twitch identity yet (NOT EXISTS anti-join — zero rows once backfilled).
        List<User> missing = await _db
            .Users.Where(u =>
                u.TwitchUserId != null
                && !_db.UserIdentities.Any(i =>
                    i.Provider == AuthEnums.Platform.Twitch && i.ProviderUserId == u.TwitchUserId
                )
            )
            .ToListAsync(ct);

        if (missing.Count == 0)
            return;

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        foreach (User user in missing)
        {
            _db.UserIdentities.Add(
                new UserIdentity
                {
                    UserId = user.Id,
                    Provider = AuthEnums.Platform.Twitch,
                    ProviderUserId = user.TwitchUserId!,
                    ProviderUsername = user.Username,
                    ProviderDisplayName = user.DisplayName,
                    ProviderAvatarUrl = user.ProfileImageUrl,
                    IsPrimary = true,
                    LinkedAt = now,
                }
            );
        }
    }
}
