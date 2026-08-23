// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// <see cref="ISessionRevocationService"/> backed by the shared <see cref="ICacheService"/> (Redis where
/// configured, in-process memory otherwise) for durable storage, fronted by a short-lived
/// <see cref="IMemoryCache"/> window so a session id re-checked on every request within that window is
/// consulted against the store at most once (S098b).
/// </summary>
public sealed class SessionRevocationService : ISessionRevocationService
{
    private const string StoreKeyPrefix = "session-revocation:";
    private const string LocalCacheKeyPrefix = "session-revocation-local:";

    /// <summary>How long a positive/negative revocation lookup is trusted locally before re-checking the store.</summary>
    private static readonly TimeSpan LocalCacheWindow = TimeSpan.FromSeconds(5);

    private readonly ICacheService _store;
    private readonly IMemoryCache _localCache;
    private readonly TimeSpan _revocationRecordLifetime;

    public SessionRevocationService(
        ICacheService store,
        IMemoryCache localCache,
        IConfiguration configuration
    )
    {
        _store = store;
        _localCache = localCache;

        // The revocation record only needs to outlive the longest-lived access token that could still be
        // carrying the revoked sid — after that the token itself fails ValidateLifetime regardless.
        double expiryMinutes = double.TryParse(
            configuration["Jwt:ExpiryMinutes"] ?? configuration["Jwt:ExpirationMinutes"],
            out double parsed
        )
            ? parsed
            : 60d;
        _revocationRecordLifetime = TimeSpan.FromMinutes(expiryMinutes) + TimeSpan.FromMinutes(5);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _store.SetAsync(
            StoreKeyPrefix + sessionId,
            true,
            _revocationRecordLifetime,
            cancellationToken
        );

        // Take effect on THIS instance immediately, without waiting for the local cache window to expire.
        _localCache.Set(LocalCacheKeyPrefix + sessionId, true, LocalCacheWindow);
    }

    public async Task<bool> IsRevokedAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default
    )
    {
        string localKey = LocalCacheKeyPrefix + sessionId;
        if (_localCache.TryGetValue(localKey, out bool cached))
            return cached;

        bool revoked = await _store.ExistsAsync(StoreKeyPrefix + sessionId, cancellationToken);
        _localCache.Set(localKey, revoked, LocalCacheWindow);
        return revoked;
    }
}
