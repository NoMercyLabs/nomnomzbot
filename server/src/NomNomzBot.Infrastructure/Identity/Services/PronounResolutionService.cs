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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Identity.Services;

/// <summary>
/// Lazy, cache-gated pronoun resolution (spec D3). Resolves once per viewer per cooldown window
/// (24 h) and writes the result back to <c>User.PronounId</c>/<c>User.AltPronounId</c> only when
/// the provider returns data and the viewer has not opted out of automatic resolution.
/// </summary>
public sealed class PronounResolutionService : IPronounResolutionService
{
    // Cache resolved viewers for 24 h — long enough to avoid hammering alejo.io on every chat message
    // while still picking up pronoun changes within a reasonable window.
    private static readonly TimeSpan ResolutionCooldown = TimeSpan.FromHours(24);
    private const string CachePrefix = "pronoun:resolve:";

    private readonly AppDbContext _db;
    private readonly IPronounProvider _provider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PronounResolutionService> _logger;

    public PronounResolutionService(
        AppDbContext db,
        IPronounProvider provider,
        IMemoryCache cache,
        ILogger<PronounResolutionService> logger
    )
    {
        _db = db;
        _provider = provider;
        _cache = cache;
        _logger = logger;
    }

    public async Task ResolveAndApplyAsync(
        Guid userId,
        string twitchLogin,
        CancellationToken ct = default
    )
    {
        string cacheKey = $"{CachePrefix}{userId}";
        if (_cache.TryGetValue(cacheKey, out _))
            return;

        // Mark as resolved immediately to prevent concurrent chat-message handlers racing on the same user.
        _cache.Set(cacheKey, true, ResolutionCooldown);

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.PronounManualOverride)
            return;

        ResolvedPronounRef? resolved = await _provider.LookupAsync(twitchLogin, ct);
        if (resolved is null)
            return;

        // Map the provider's string key (e.g. "theythem") to the DB row id.
        int? primaryId = await FindPronounIdByKeyAsync(resolved.PronounKey, ct);
        if (primaryId is null)
        {
            _logger.LogDebug(
                "Pronoun key '{Key}' from alejo not found in R.1 catalog; skipping for {Login}.",
                resolved.PronounKey,
                twitchLogin
            );
            return;
        }

        int? altId = resolved.AltPronounKey is not null
            ? await FindPronounIdByKeyAsync(resolved.AltPronounKey, ct)
            : null;

        user.PronounId = primaryId;
        user.AltPronounId = altId;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<int?> FindPronounIdByKeyAsync(string key, CancellationToken ct)
    {
        Pronoun? pronoun = await _db.Pronouns.FirstOrDefaultAsync(p => p.Key == key, ct);
        return pronoun?.Id;
    }
}
