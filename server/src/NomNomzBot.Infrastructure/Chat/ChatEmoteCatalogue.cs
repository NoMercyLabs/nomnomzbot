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
/// (app token, no scope), and the logged-in OPERATOR's cross-channel emotes via Get User Emotes on the OPERATOR's
/// OWN token (<c>user:read:emotes</c>) so a moderator's personal subscription emotes reach their composer on ANY
/// channel they operate — not only their own — keyed to the operator so they never leak between operators. Twitch
/// emote urls are built from the deterministic CDN template — the exact shape <c>TwitchEmoteUrlAdapter</c> uses for
/// the live feed — so the composer renders Twitch emotes identically. Cache-only for third-party (never a provider
/// HTTP call here); any Twitch fetch failure (including a missing user-emotes scope, or an operator with no linked
/// Twitch identity) degrades that source to empty rather than failing the catalogue.
/// </summary>
public sealed class ChatEmoteCatalogue : IChatEmoteCatalogue
{
    private static readonly TimeSpan TwitchGlobalTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan TwitchChannelTtl = TimeSpan.FromHours(1);

    // The user's cross-channel emotes track subscription changes; a short TTL matching the channel set keeps them
    // fresh without a per-request fetch.
    private static readonly TimeSpan TwitchUserTtl = TwitchChannelTtl;

    // Get User Emotes is cursor-paged; cap the walk so a pathological account can never spin the catalogue. A hit is
    // logged (never silent truncation) — 10 × 100 emotes is far beyond any real user's reachable set.
    private const int MaxUserEmotePages = 10;

    // The cross-channel follower pass makes one Get Channel Emotes call per DISTINCT other channel the operator has
    // emotes from; bound the fan-out so a pathological account (following thousands of channels) can never spin the
    // catalogue. Beyond this cap the pass is truncated (logged, never silent).
    private const int MaxFollowerEmoteChannels = 60;

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
        Guid operatorUserId,
        ChatEmoteSender sender = ChatEmoteSender.Operator,
        CancellationToken ct = default
    )
    {
        string? twitchId = await _identity.GetTwitchChannelIdAsync(broadcasterId, ct);

        // Add in precedence order (channel, then the operator's cross-channel set, then global; Twitch before
        // third-party); dedup keeps the first. The operator's set rides THEIR own token and is keyed to THEM —
        // the channel's Twitch id (when known) travels as the optional broadcaster_id so this channel's follower
        // emotes the operator has are included too, but the emotes returned are the operator's, not the tenant's.
        //
        // When composing AS THE BOT the two operator-scoped Twitch sources are skipped: the channel's Twitch
        // emotes are subscriber-gated (the dedicated bot account is not the broadcaster and cannot use them) and
        // the operator's personal emotes obviously are not the bot's. What survives — Twitch global + third-party
        // (BTTV/FFZ/7TV are plain text codes any sender may type) — is exactly what the bot can genuinely send.
        List<ChatEmote> all = new();
        if (sender == ChatEmoteSender.Operator)
        {
            all.AddRange(await TwitchChannelAsync(broadcasterId, twitchId, ct));
            all.AddRange(await TwitchUserEmotesAsync(operatorUserId, twitchId, ct));
        }
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

    // The logged-in OPERATOR's emotes across every channel they can use them in (subs, follower rewards, bits
    // tiers, global) — Get User Emotes on the OPERATOR's OWN token, so a moderator's personal emotes are correct
    // on any channel they operate, not only their own. Cursor-paged: follow NextCursor until exhausted or the
    // page cap. Keyed to the OPERATOR's resolved Twitch id (never the tenant's) so one operator's subscription
    // emotes never leak into another operator's composer on the same channel; a missing user:read:emotes scope,
    // an operator with no linked Twitch identity, or any fetch failure degrades this source to empty.
    private async Task<IReadOnlyList<ChatEmote>> TwitchUserEmotesAsync(
        Guid operatorUserId,
        string? broadcasterTwitchId,
        CancellationToken ct
    )
    {
        // The cache key is the OPERATOR's Twitch id — resolved here so a cache hit never rides another actor's
        // key. No linked Twitch identity ⇒ no personal emotes to show; degrade this source to empty.
        string? operatorTwitchId = await _identity.GetTwitchUserIdAsync(operatorUserId, ct);
        if (string.IsNullOrEmpty(operatorTwitchId))
            return [];

        string key = ChatEmoteCacheKeys.TwitchUser(operatorTwitchId);
        IReadOnlyList<ChatEmote>? cached = await _cache.GetAsync<IReadOnlyList<ChatEmote>>(key, ct);
        if (cached is not null)
            return cached;

        List<ChatEmote> mapped = new();

        // Every OTHER channel the operator already has emotes from. Get User Emotes returns a channel's FOLLOWER
        // emotes only when THAT channel is passed as broadcaster_id, so cross-channel follower emotes are absent
        // from the base walk — they are back-filled per channel below (Twitch's own picker fetches follower emotes
        // per channel). The current channel is excluded (its follower emotes already came through broadcaster_id).
        HashSet<string> otherOwnerIds = new(StringComparer.Ordinal);
        string? cursor = null;
        int page = 0;

        do
        {
            Result<TwitchPage<TwitchUserEmote>> fetched =
                await _assets.GetUserEmotesAsOperatorAsync(
                    operatorUserId,
                    broadcasterTwitchId,
                    cursor,
                    ct
                );
            if (fetched.IsFailure)
            {
                _logger.LogDebug(
                    "ChatEmoteCatalogue: Twitch user emotes fetch failed for operator {OperatorUserId}, omitting",
                    operatorUserId
                );
                return [];
            }

            foreach (TwitchUserEmote emote in fetched.Value.Items)
            {
                mapped.Add(Map(emote.Id, emote.Name, emote.Format, emote.EmoteSetId));

                if (
                    !string.IsNullOrEmpty(emote.OwnerId)
                    && !string.Equals(emote.OwnerId, broadcasterTwitchId, StringComparison.Ordinal)
                )
                    otherOwnerIds.Add(emote.OwnerId);
            }

            cursor = fetched.Value.NextCursor;
            page++;
        } while (cursor is not null && page < MaxUserEmotePages);

        if (cursor is not null)
            _logger.LogDebug(
                "ChatEmoteCatalogue: Twitch user emotes for operator {OperatorUserId} hit the {MaxPages}-page cap; the list is truncated",
                operatorUserId,
                MaxUserEmotePages
            );

        await AddCrossChannelFollowerEmotesAsync(mapped, otherOwnerIds, ct);

        await _cache.SetAsync<IReadOnlyList<ChatEmote>>(key, mapped, TwitchUserTtl, ct);
        return mapped;
    }

    // Back-fills the operator's FOLLOWER emotes from every OTHER channel they have emotes from. Get User Emotes only
    // surfaces a channel's follower emotes when that channel is the broadcaster_id, so for each distinct other owner
    // we fetch that channel's emotes (Get Channel Emotes — App token, one call, no pagination) and keep only the
    // follower-type ones. Bounded to MaxFollowerEmoteChannels; a per-channel failure degrades to skipping that
    // channel, never failing the whole source. These are part of the operator's usable set, so they cache together
    // with the base user emotes under the same key; the catalogue-level Dedup resolves any code collisions.
    private async Task AddCrossChannelFollowerEmotesAsync(
        List<ChatEmote> mapped,
        IReadOnlyCollection<string> otherOwnerIds,
        CancellationToken ct
    )
    {
        IEnumerable<string> channels = otherOwnerIds;
        if (otherOwnerIds.Count > MaxFollowerEmoteChannels)
        {
            _logger.LogDebug(
                "ChatEmoteCatalogue: operator has emotes from {Count} channels; bounding the follower pass to the first {Max}",
                otherOwnerIds.Count,
                MaxFollowerEmoteChannels
            );
            channels = otherOwnerIds.Take(MaxFollowerEmoteChannels);
        }

        foreach (string ownerId in channels)
        {
            Result<IReadOnlyList<TwitchChannelEmote>> fetched =
                await _assets.GetChannelEmotesByTwitchIdAsync(ownerId, ct);
            if (fetched.IsFailure)
            {
                _logger.LogDebug(
                    "ChatEmoteCatalogue: channel emotes fetch failed for {OwnerId}, skipping its follower emotes",
                    ownerId
                );
                continue;
            }

            foreach (TwitchChannelEmote emote in fetched.Value)
                if (string.Equals(emote.EmoteType, "follower", StringComparison.Ordinal))
                    mapped.Add(Map(emote.Id, emote.Name, emote.Format, emote.EmoteSetId));
        }
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

    // Dedup by code, CASE-SENSITIVE, first-wins. Emote codes are case-sensitive everywhere — Twitch "Kappa", and
    // on 7TV/BTTV/FFZ "Pog", "POG" and "pog" are three distinct emotes — so only an exact-case repeat is a true
    // collision, which the precedence-ordered add above then resolves; differently-cased codes all survive.
    private static IReadOnlyList<ChatEmote> Dedup(IEnumerable<ChatEmote> emotes)
    {
        Dictionary<string, ChatEmote> byCode = new(StringComparer.Ordinal);
        foreach (ChatEmote emote in emotes)
            byCode.TryAdd(emote.Code, emote);
        return byCode.Values.ToList();
    }
}
