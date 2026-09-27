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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// See <see cref="IPlatformBuiltinReplyDefaults"/>. The table is tiny and global, so the whole set is read at
/// once and kept in the shared <see cref="ICacheService"/> (Redis on full/SaaS, memory on lite) — every API
/// instance sees an admin save as soon as the editor drops the key. Singleton: it opens its own scope for the
/// database read because the composer that calls it is a singleton too.
/// </summary>
public sealed class PlatformBuiltinReplyDefaultsReader(
    IServiceScopeFactory scopeFactory,
    ICacheService cache
) : IPlatformBuiltinReplyDefaults
{
    private const string CacheKey = "platform-defaults:builtin-replies";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<string?> GetAsync(
        string builtinKey,
        string slot,
        CancellationToken ct = default
    )
    {
        Dictionary<string, string> texts = await LoadAsync(ct);
        return texts.GetValueOrDefault(Key(builtinKey, slot));
    }

    public Task InvalidateAsync(CancellationToken ct = default) => cache.RemoveAsync(CacheKey, ct);

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        Dictionary<string, string>? cached = await cache.GetAsync<Dictionary<string, string>>(
            CacheKey,
            ct
        );
        if (cached is not null)
            return cached;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        Dictionary<string, string> texts = await db
            .PlatformBuiltinReplyDefaults.AsNoTracking()
            .ToDictionaryAsync(d => Key(d.BuiltinKey, d.Slot), d => d.Template, ct);
        await cache.SetAsync(CacheKey, texts, CacheLifetime, ct);
        return texts;
    }

    private static string Key(string builtinKey, string slot) => builtinKey + "|" + slot;
}
