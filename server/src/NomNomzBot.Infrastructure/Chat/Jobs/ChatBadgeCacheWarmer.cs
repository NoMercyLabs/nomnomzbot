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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Warms the Helix badge cache the decoration pipeline reads (chat-decoration spec §3.6/§7). It fetches the global and
/// per-channel chat-badge sets through the Helix client and writes them under <see cref="ChatBadgeCacheKeys"/>.
/// Best-effort and last-good: a failed fetch writes nothing, leaving the previously cached set in place. Scoped — it
/// uses the scoped <see cref="ITwitchHelixClient"/> — so the refresh worker resolves it inside a per-iteration scope.
/// </summary>
public sealed class ChatBadgeCacheWarmer
{
    private static readonly TimeSpan GlobalTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChannelTtl = TimeSpan.FromHours(1);

    private readonly ITwitchHelixClient _helix;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatBadgeCacheWarmer> _logger;

    public ChatBadgeCacheWarmer(
        ITwitchHelixClient helix,
        ICacheService cache,
        ILogger<ChatBadgeCacheWarmer> logger
    )
    {
        _helix = helix;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Refreshes the global badge set into cache; returns whether it was warmed (false keeps the last-good set).</summary>
    public async Task<bool> WarmGlobalAsync(CancellationToken ct = default) =>
        await CacheOrKeepAsync(
            await _helix.ChatAssets.GetGlobalChatBadgesAsync(ct),
            ChatBadgeCacheKeys.Global,
            GlobalTtl,
            "global",
            ct
        );

    /// <summary>Refreshes one channel's badge set into cache; returns whether it was warmed (false keeps the last-good set).</summary>
    public async Task<bool> WarmChannelAsync(Guid broadcasterId, CancellationToken ct = default) =>
        await CacheOrKeepAsync(
            await _helix.ChatAssets.GetChannelChatBadgesAsync(broadcasterId, ct),
            ChatBadgeCacheKeys.Channel(broadcasterId),
            ChannelTtl,
            $"channel {broadcasterId}",
            ct
        );

    private async Task<bool> CacheOrKeepAsync(
        Result<IReadOnlyList<TwitchChatBadgeSet>> result,
        string cacheKey,
        TimeSpan ttl,
        string scope,
        CancellationToken ct
    )
    {
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Badge refresh ({Scope}) failed: {Error}. Keeping last-good cache.",
                scope,
                result.ErrorMessage
            );
            return false;
        }

        await _cache.SetAsync(cacheKey, result.Value, ttl, ct);
        return true;
    }
}
