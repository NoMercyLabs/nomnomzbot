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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The uncached read behind <see cref="IHubUserEnricher"/>: one <c>Users</c> lookup by <c>TwitchUserId</c>
/// (with the pronoun pair included so the display string can be formatted) plus one optional
/// <c>ChannelCommunityStandings</c> lookup for this broadcaster/viewer pair. Returns <c>null</c> only when the
/// viewer has no internal <c>User</c> row at all (never seen by this bot instance yet) — every other field is
/// independently nullable on the returned <see cref="HubUserEnrichment"/>.
/// </summary>
public sealed class HubUserEnrichmentStore(IApplicationDbContext db) : IHubUserEnrichmentStore
{
    public async Task<HubUserEnrichment?> LoadAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    )
    {
        User? user = await db
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .FirstOrDefaultAsync(u => u.TwitchUserId == twitchUserId, ct);

        if (user is null)
            return null;

        // Nullable projection: distinguishes "no standing row for this viewer in this channel" (null) from the
        // explicit CommunityStanding.Everyone value (a real, recorded row at the floor of the ladder).
        CommunityStanding? standing = await db
            .ChannelCommunityStandings.Where(s =>
                s.BroadcasterId == broadcasterId && s.UserId == user.Id
            )
            .Select(s => (CommunityStanding?)s.Standing)
            .FirstOrDefaultAsync(ct);

        return new HubUserEnrichment(
            user.DisplayName,
            user.ProfileImageUrl,
            UserPronounDisplay.Format(user.Pronoun, user.AltPronoun),
            standing?.ToString()
        );
    }
}
