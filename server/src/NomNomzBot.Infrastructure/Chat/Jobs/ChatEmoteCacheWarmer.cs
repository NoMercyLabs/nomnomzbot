// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Warms the third-party emote cache the decoration pipeline reads (chat-decoration spec §3.6/§7). It fetches each
/// provider's set through the resilient client and writes it to <see cref="ICacheService"/> under the shared
/// <see cref="ChatEmoteCacheKeys"/>. Best-effort and <b>last-good</b>: a provider failure writes nothing, so the
/// previously cached (stale-but-good) set survives untouched — warming never throws into the host or wipes the cache.
/// </summary>
public sealed class ChatEmoteCacheWarmer
{
    // The cache TTL is the stale ceiling if the worker stops; while it runs, each refresh resets it (spec §7).
    private static readonly TimeSpan GlobalTtl = TimeSpan.FromHours(6);

    private readonly IThirdPartyEmoteProviderRegistry _registry;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatEmoteCacheWarmer> _logger;

    public ChatEmoteCacheWarmer(
        IThirdPartyEmoteProviderRegistry registry,
        ICacheService cache,
        ILogger<ChatEmoteCacheWarmer> logger
    )
    {
        _registry = registry;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Refreshes every provider's GLOBAL emote set into cache; returns how many providers were warmed.</summary>
    public async Task<int> WarmGlobalAsync(CancellationToken ct = default)
    {
        int warmed = 0;

        foreach (IThirdPartyEmoteProvider provider in _registry.All)
        {
            Result<IReadOnlyList<ChatEmote>> result = await provider.GetGlobalAsync(ct);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Global emote refresh for {Provider} failed: {Error}. Keeping last-good cache.",
                    provider.Provider,
                    result.ErrorMessage
                );
                continue;
            }

            await _cache.SetAsync(
                ChatEmoteCacheKeys.Global(provider.Provider),
                result.Value,
                GlobalTtl,
                ct
            );
            warmed++;
        }

        return warmed;
    }
}
