// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
