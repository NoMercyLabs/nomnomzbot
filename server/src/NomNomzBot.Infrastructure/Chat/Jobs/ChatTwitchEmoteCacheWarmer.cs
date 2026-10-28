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

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Jobs;

/// <summary>
/// Warms the Twitch emote sets the decoration pipeline and the catalogue read (type, tier, owner). It fetches the
/// global set and a channel's set through the Helix client and caches them under the same keys the catalogue uses
/// (<see cref="ChatEmoteCacheKeys"/>). Each owner's login and display name is fetched once with Get Users and cached
/// under <see cref="ChatEmoteCacheKeys.TwitchOwner"/>, so chat decoration never calls Helix. A failed fetch writes
/// nothing (last-good preserved). Scoped, like the other Helix warmers.
/// </summary>
public sealed class ChatTwitchEmoteCacheWarmer
{
    private static readonly TimeSpan GlobalTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChannelTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan OwnerTtl = TimeSpan.FromHours(24);

    private readonly ITwitchHelixClient _helix;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatTwitchEmoteCacheWarmer> _logger;

    public ChatTwitchEmoteCacheWarmer(
        ITwitchHelixClient helix,
        ICacheService cache,
        ILogger<ChatTwitchEmoteCacheWarmer> logger
    )
    {
        _helix = helix;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Refreshes the global Twitch emotes into cache; returns whether it was warmed (false keeps the last-good set).</summary>
    public async Task<bool> WarmGlobalAsync(CancellationToken ct = default)
    {
        Result<IReadOnlyList<TwitchGlobalEmote>> result =
            await _helix.ChatAssets.GetGlobalEmotesAsync(ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Twitch global emote refresh failed: {Error}. Keeping last-good cache.",
                result.ErrorMessage
            );
            return false;
        }

        List<ChatEmote> mapped = [.. result.Value.Select(TwitchEmoteMapper.FromGlobal)];
        await _cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.Twitch),
            mapped,
            GlobalTtl,
            ct
        );
        return true;
    }

    /// <summary>Refreshes one channel's Twitch emotes into cache; returns whether it was warmed (false keeps the last-good set).</summary>
    public async Task<bool> WarmChannelAsync(
        Guid broadcasterId,
        string twitchBroadcasterId,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<TwitchChannelEmote>> result =
            await _helix.ChatAssets.GetChannelEmotesAsync(broadcasterId, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Twitch channel emote refresh (channel {Channel}) failed: {Error}. Keeping last-good cache.",
                broadcasterId,
                result.ErrorMessage
            );
            return false;
        }

        ChatEmoteOwner? owner = await ResolveOwnerAsync(twitchBroadcasterId, ct);
        List<ChatEmote> mapped =
        [
            .. result.Value.Select(e =>
                TwitchEmoteMapper.FromChannel(e, twitchBroadcasterId, owner)
            ),
        ];
        await _cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, twitchBroadcasterId),
            mapped,
            ChannelTtl,
            ct
        );
        return true;
    }

    // One Get Users call per owner: a cached owner is returned as is; a failed or empty lookup leaves the owner null
    // (the emote still carries the owner id) and is retried at the next channel warm.
    private async Task<ChatEmoteOwner?> ResolveOwnerAsync(string twitchUserId, CancellationToken ct)
    {
        string key = ChatEmoteCacheKeys.TwitchOwner(twitchUserId);
        ChatEmoteOwner? cached = await _cache.GetAsync<ChatEmoteOwner>(key, ct);
        if (cached is not null)
            return cached;

        Result<IReadOnlyList<TwitchUser>> result = await _helix.Users.GetUsersByIdsAsync(
            [twitchUserId],
            ct
        );
        TwitchUser? user = result.IsSuccess ? result.Value.FirstOrDefault() : null;
        if (user is null)
        {
            _logger.LogDebug("Twitch emote owner {OwnerId} could not be looked up.", twitchUserId);
            return null;
        }

        ChatEmoteOwner owner = new(
            user.Id,
            user.Login,
            user.DisplayName,
            $"https://www.twitch.tv/{user.Login}",
            string.IsNullOrEmpty(user.ProfileImageUrl) ? null : user.ProfileImageUrl
        );
        await _cache.SetAsync(key, owner, OwnerTtl, ct);
        return owner;
    }
}
