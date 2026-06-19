// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;

namespace NomNomzBot.Infrastructure.Platform.Caching;

public sealed class DistributedCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheService> _logger;

    public DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        byte[]? bytes = await _cache.GetAsync(key, ct);
        if (bytes is null)
            return default;
        return JsonSerializer.Deserialize<T>(bytes);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken ct = default
    )
    {
        DistributedCacheEntryOptions options = new();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);
        else
            options.SetSlidingExpiration(TimeSpan.FromMinutes(5));

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await _cache.SetAsync(key, bytes, options, ct);
        _logger.LogTrace(
            "Cache SET: {Key} (expiry={Expiry})",
            key,
            expiry?.ToString() ?? "sliding:5m"
        );
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(key, ct);
        _logger.LogTrace("Cache REMOVE: {Key}", key);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        byte[]? bytes = await _cache.GetAsync(key, ct);
        return bytes is not null;
    }
}
