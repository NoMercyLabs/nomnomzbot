// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Providers;

/// <inheritdoc cref="ISevenTvUserPaintResolver" />
/// <remarks>
/// Calls <c>GET https://7tv.io/v3/users/twitch/{id}</c> — the same endpoint <see cref="SevenTvEmoteProvider"/>
/// already calls per CHANNEL for that channel's emote set, but this calls it per CHATTER, so a busy chat would
/// otherwise fire one request per message per unique chatter. Cache-gated the same way
/// <c>HubUserEnricher</c> gates DB reads: short TTL, long enough to collapse a burst of messages from the same
/// chatter, short enough that a newly-equipped paint shows up without a restart. Verified live 2026-09-04: the
/// paint id lives at <c>user.style.paint_id</c> and is null/absent for a chatter wearing none.
/// </remarks>
public sealed class SevenTvUserPaintResolver : ISevenTvUserPaintResolver
{
    private const string UserUrlPrefix = "https://7tv.io/v3/users/twitch/";
    private const string CacheKeyPrefix = "7tv:user-paint:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISevenTvPaintCatalogue _catalogue;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SevenTvUserPaintResolver> _logger;

    public SevenTvUserPaintResolver(
        IHttpClientFactory httpClientFactory,
        ISevenTvPaintCatalogue catalogue,
        IMemoryCache cache,
        ILogger<SevenTvUserPaintResolver> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _catalogue = catalogue;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatPaint?> ResolveAsync(
        string twitchUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
            return null;

        string cacheKey = $"{CacheKeyPrefix}{twitchUserId}";
        if (_cache.TryGetValue(cacheKey, out ChatPaint? cached))
            return cached;

        string? paintId = await FetchPaintIdAsync(twitchUserId, cancellationToken);
        ChatPaint? paint = paintId is null
            ? null
            : await _catalogue.GetAsync(paintId, cancellationToken);

        // Cache both hits and misses: a chatter wearing no paint is looked up just as often as one who does,
        // and re-asking 7TV every message for someone with none would defeat the point of caching at all.
        _cache.Set(cacheKey, paint, CacheTtl);
        return paint;
    }

    private async Task<string?> FetchPaintIdAsync(
        string twitchUserId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ChatEmoteHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(
                $"{UserUrlPrefix}{twitchUserId}",
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParsePaintId(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cosmetic is decoration. It must never cost a chatter their message, so every failure here is
            // swallowed to a debug line and the caller simply gets no paint.
            _logger.LogDebug(ex, "7TV user paint lookup failed for {TwitchUserId}", twitchUserId);
            return null;
        }
    }

    /// <summary>Internal so the wire shape is tested against a real captured payload.</summary>
    internal static string? ParsePaintId(string json)
    {
        SevenTvUserPaintResponse? response =
            JsonConvert.DeserializeObject<SevenTvUserPaintResponse>(json);
        string? paintId = response?.User?.Style?.PaintId;
        return string.IsNullOrEmpty(paintId) ? null : paintId;
    }

    private sealed class SevenTvUserPaintResponse
    {
        public SevenTvUserPaintUser? User { get; set; }
    }

    private sealed class SevenTvUserPaintUser
    {
        public SevenTvUserPaintStyle? Style { get; set; }
    }

    private sealed class SevenTvUserPaintStyle
    {
        [JsonProperty("paint_id")]
        public string? PaintId { get; set; }
    }
}
