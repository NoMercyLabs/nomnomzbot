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
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Builds the channel emote catalogue (chat-client.md §3.2). BTTV/FFZ/7TV come straight from the warm decoration
/// cache (chat-decoration §7); the Twitch global + channel sets are fetched via <see cref="ITwitchChatAssetsApi"/>
/// (app token, no scope) and cached under the same key scheme. Twitch emote urls are built from the deterministic
/// CDN template — the exact shape <c>TwitchEmoteUrlAdapter</c> uses for the live feed — so the composer renders
/// Twitch emotes identically. Cache-only for third-party (never a provider HTTP call here); a Twitch fetch failure
/// degrades that source to empty rather than failing the catalogue.
/// </summary>
public sealed class ChatEmoteCatalogue : IChatEmoteCatalogue
{
    private static readonly TimeSpan TwitchGlobalTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan TwitchChannelTtl = TimeSpan.FromHours(1);

    // Twitch emote CDN v2: /{id}/{format}/{theme}/{scale}. Dark theme + the three offered scales — mirrors
    // TwitchEmoteUrlAdapter so a Twitch emote in the composer matches the same emote in the feed.
    private static readonly (string Key, string Scale)[] Scales =
    [
        ("1", "1.0"),
        ("2", "2.0"),
        ("3", "3.0"),
    ];

    // Precedence low-to-high is applied by add order below (first-wins dedup): Twitch native before third-party.
    private static readonly EmoteProvider[] ThirdParty =
    [
        EmoteProvider.SevenTv,
        EmoteProvider.Bttv,
        EmoteProvider.Ffz,
    ];

    private readonly ICacheService _cache;
    private readonly ITwitchChatAssetsApi _assets;
    private readonly ITwitchIdentityResolver _identity;
    private readonly ILogger<ChatEmoteCatalogue> _logger;

    public ChatEmoteCatalogue(
        ICacheService cache,
        ITwitchChatAssetsApi assets,
        ITwitchIdentityResolver identity,
        ILogger<ChatEmoteCatalogue> logger
    )
    {
        _cache = cache;
        _assets = assets;
        _identity = identity;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ChatEmote>>> GetForChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        string? twitchId = await _identity.GetTwitchChannelIdAsync(broadcasterId, ct);

        // Add in precedence order (channel before global, Twitch before third-party); dedup keeps the first.
        List<ChatEmote> all = new();
        all.AddRange(await TwitchChannelAsync(broadcasterId, twitchId, ct));
        all.AddRange(await TwitchGlobalAsync(ct));

        if (twitchId is not null)
            foreach (EmoteProvider provider in ThirdParty)
                all.AddRange(await CachedAsync(ChatEmoteCacheKeys.Channel(provider, twitchId), ct));

        foreach (EmoteProvider provider in ThirdParty)
            all.AddRange(await CachedAsync(ChatEmoteCacheKeys.Global(provider), ct));

        return Result.Success(Dedup(all));
    }

    private async Task<IReadOnlyList<ChatEmote>> CachedAsync(string key, CancellationToken ct) =>
        await _cache.GetAsync<IReadOnlyList<ChatEmote>>(key, ct) ?? [];

    private async Task<IReadOnlyList<ChatEmote>> TwitchGlobalAsync(CancellationToken ct)
    {
        string key = ChatEmoteCacheKeys.Global(EmoteProvider.Twitch);
        IReadOnlyList<ChatEmote>? cached = await _cache.GetAsync<IReadOnlyList<ChatEmote>>(key, ct);
        if (cached is not null)
            return cached;

        Result<IReadOnlyList<TwitchGlobalEmote>> fetched = await _assets.GetGlobalEmotesAsync(ct);
        if (fetched.IsFailure)
        {
            _logger.LogDebug("ChatEmoteCatalogue: Twitch global emotes fetch failed, omitting");
            return [];
        }

        List<ChatEmote> mapped = fetched
            .Value.Select(e => Map(e.Id, e.Name, e.Format, setId: null))
            .ToList();
        await _cache.SetAsync<IReadOnlyList<ChatEmote>>(key, mapped, TwitchGlobalTtl, ct);
        return mapped;
    }

    private async Task<IReadOnlyList<ChatEmote>> TwitchChannelAsync(
        Guid broadcasterId,
        string? twitchId,
        CancellationToken ct
    )
    {
        if (twitchId is null)
            return [];

        string key = ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, twitchId);
        IReadOnlyList<ChatEmote>? cached = await _cache.GetAsync<IReadOnlyList<ChatEmote>>(key, ct);
        if (cached is not null)
            return cached;

        Result<IReadOnlyList<TwitchChannelEmote>> fetched = await _assets.GetChannelEmotesAsync(
            broadcasterId,
            ct
        );
        if (fetched.IsFailure)
        {
            _logger.LogDebug(
                "ChatEmoteCatalogue: Twitch channel emotes fetch failed for {BroadcasterId}, omitting",
                broadcasterId
            );
            return [];
        }

        List<ChatEmote> mapped = fetched
            .Value.Select(e => Map(e.Id, e.Name, e.Format, e.EmoteSetId))
            .ToList();
        await _cache.SetAsync<IReadOnlyList<ChatEmote>>(key, mapped, TwitchChannelTtl, ct);
        return mapped;
    }

    private static ChatEmote Map(
        string id,
        string code,
        IReadOnlyList<string> formats,
        string? setId
    )
    {
        bool animated = formats.Contains("animated");
        string format = animated ? "animated" : "static";

        Dictionary<string, string> urls = new(Scales.Length);
        foreach ((string key, string scale) in Scales)
            urls[key] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/{format}/dark/{scale}";

        return new ChatEmote(
            EmoteProvider.Twitch,
            id,
            code,
            urls,
            animated,
            ZeroWidth: false,
            SetId: setId,
            Formats: formats
        );
    }

    // Dedup by code, case-insensitive, first-wins — so the precedence-ordered add above decides collisions.
    private static IReadOnlyList<ChatEmote> Dedup(IEnumerable<ChatEmote> emotes)
    {
        Dictionary<string, ChatEmote> byCode = new(StringComparer.OrdinalIgnoreCase);
        foreach (ChatEmote emote in emotes)
            byCode.TryAdd(emote.Code, emote);
        return byCode.Values.ToList();
    }
}
