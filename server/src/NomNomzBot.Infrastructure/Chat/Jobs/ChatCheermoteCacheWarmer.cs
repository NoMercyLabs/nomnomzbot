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
/// Warms the Helix cheermote cache the decoration pipeline reads (chat-decoration spec §3.6/§7). It fetches a channel's
/// cheermotes through the Helix client and caches them under <see cref="ChatCheermoteCacheKeys"/>; a failed fetch writes
/// nothing (last-good preserved). Scoped — it uses the scoped <see cref="ITwitchHelixClient"/> — so the refresh worker
/// resolves it inside a per-iteration scope.
/// </summary>
public sealed class ChatCheermoteCacheWarmer
{
    private static readonly TimeSpan ChannelTtl = TimeSpan.FromHours(1);

    private readonly ITwitchHelixClient _helix;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatCheermoteCacheWarmer> _logger;

    public ChatCheermoteCacheWarmer(
        ITwitchHelixClient helix,
        ICacheService cache,
        ILogger<ChatCheermoteCacheWarmer> logger
    )
    {
        _helix = helix;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Refreshes one channel's cheermotes into cache; returns whether it was warmed (false keeps the last-good set).</summary>
    public async Task<bool> WarmChannelAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        Result<IReadOnlyList<TwitchCheermote>> result = await _helix.Bits.GetCheermotesAsync(
            broadcasterId,
            ct
        );
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Cheermote refresh (channel {Channel}) failed: {Error}. Keeping last-good cache.",
                broadcasterId,
                result.ErrorMessage
            );
            return false;
        }

        await _cache.SetAsync(
            ChatCheermoteCacheKeys.Channel(broadcasterId),
            result.Value,
            ChannelTtl,
            ct
        );
        return true;
    }
}
