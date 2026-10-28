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

using Microsoft.Extensions.Caching.Memory;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The per-process cache of the outside sources, keyed by source and query (catalog section 9, rule 3). A
/// failed read is never kept, so the next render tries the source again. One cache per process also keeps all
/// channels together under the provider's limit.
/// </summary>
internal sealed class LookupCache(IMemoryCache memory)
{
    public async Task<T?> GetOrFetchAsync<T>(
        string source,
        string query,
        TimeSpan lifetime,
        Func<Task<T?>> fetch
    )
        where T : class
    {
        string key = $"lookup:{source}:{query.ToLowerInvariant()}";
        if (memory.TryGetValue(key, out T? cached))
            return cached;

        T? value = await fetch();
        if (value is not null)
            memory.Set(key, value, lifetime);
        return value;
    }
}
