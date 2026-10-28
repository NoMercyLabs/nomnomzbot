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

using Newtonsoft.Json;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Providers;

/// <summary>
/// FrankerFaceZ emote adapter (chat-decoration spec §3.2/§8). Global set from <c>/set/global</c> (the
/// <c>default_sets</c> ids into <c>sets</c>); a channel's set from <c>/room/{login}</c> keyed by the broadcaster
/// LOGIN, not the id. FFZ ships its own scale-keyed urls (reused verbatim, protocol normalised to https);
/// <c>modifier</c> emotes are zero-width overlays; an <c>animated</c> url set marks an animated emote.
/// </summary>
public sealed class FfzEmoteProvider : IThirdPartyEmoteProvider
{
    private const string GlobalUrl = "https://api.frankerfacez.com/v1/set/global";
    private const string RoomUrl = "https://api.frankerfacez.com/v1/room/";

    private readonly IHttpClientFactory _httpClientFactory;

    public FfzEmoteProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmoteProvider Provider => EmoteProvider.Ffz;

    public Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(CancellationToken ct = default) =>
        EmoteHttpFetch.GetAsync(_httpClientFactory, GlobalUrl, ParseGlobal, "FFZ", ct);

    public Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
        string twitchBroadcasterId,
        string broadcasterLogin,
        CancellationToken ct = default
    ) =>
        EmoteHttpFetch.GetAsync(
            _httpClientFactory,
            $"{RoomUrl}{broadcasterLogin}",
            ParseRoom,
            "FFZ",
            ct
        );

    // ─── Parsing (internal for behaviour tests) ───────────────────────────────

    internal static IReadOnlyList<ChatEmote> ParseGlobal(string json)
    {
        FfzGlobalResponse? response = JsonConvert.DeserializeObject<FfzGlobalResponse>(json);
        if (response is null)
            return [];

        List<FfzEmoticon> emoticons = [];
        foreach (int setId in response.DefaultSets)
            if (response.Sets.TryGetValue(setId.ToString(), out FfzSet? set))
                emoticons.AddRange(set.Emoticons);

        return Map(emoticons, "global");
    }

    internal static IReadOnlyList<ChatEmote> ParseRoom(string json)
    {
        FfzRoomResponse? response = JsonConvert.DeserializeObject<FfzRoomResponse>(json);
        if (response is null)
            return [];

        List<FfzEmoticon> emoticons = [];
        foreach (FfzSet set in response.Sets.Values)
            emoticons.AddRange(set.Emoticons);

        return Map(emoticons, "channel");
    }

    private static ChatEmoteOwner? MapUser(FfzUser? user) =>
        user is null || user.Id == 0
            ? null
            : new(
                user.Id.ToString(),
                user.Name,
                user.DisplayName,
                $"https://www.frankerfacez.com/{user.Name}"
            );

    private static IReadOnlyList<ChatEmote> Map(IReadOnlyList<FfzEmoticon> emoticons, string type)
    {
        List<ChatEmote> result = new(emoticons.Count);
        foreach (FfzEmoticon emoticon in emoticons)
        {
            if (emoticon.Id == 0 || string.IsNullOrEmpty(emoticon.Name))
                continue;

            bool animated = emoticon.Animated is { Count: > 0 };
            IReadOnlyDictionary<string, string> source = animated
                ? emoticon.Animated!
                : emoticon.Urls;
            Dictionary<string, string> urls = Normalize(source);
            // FFZ's plain urls are the still image; the "animated" set is separate.
            Dictionary<string, string> staticUrls = animated ? Normalize(emoticon.Urls) : urls;

            result.Add(
                new(
                    EmoteProvider.Ffz,
                    emoticon.Id.ToString(),
                    emoticon.Name,
                    urls,
                    animated,
                    emoticon.Modifier,
                    Type: type,
                    Owner: MapUser(emoticon.Owner),
                    Artist: MapUser(emoticon.Artist),
                    StaticUrls: staticUrls
                )
            );
        }

        return result;
    }

    // FFZ ships protocol-relative urls ("//cdn.frankerfacez.com/..."); pin them to https.
    private static string Normalize(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;

    private static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> source)
    {
        Dictionary<string, string> urls = new(source.Count);
        foreach (KeyValuePair<string, string> entry in source)
            urls[entry.Key] = Normalize(entry.Value);

        return urls;
    }

    // ─── Provider response shapes (Newtonsoft) ────────────────────────────────

    private sealed class FfzGlobalResponse
    {
        [JsonProperty("default_sets")]
        public List<int> DefaultSets { get; set; } = [];

        public Dictionary<string, FfzSet> Sets { get; set; } = new();
    }

    private sealed class FfzRoomResponse
    {
        public Dictionary<string, FfzSet> Sets { get; set; } = new();
    }

    private sealed class FfzSet
    {
        public List<FfzEmoticon> Emoticons { get; set; } = [];
    }

    private sealed class FfzEmoticon
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string> Urls { get; set; } = new();
        public Dictionary<string, string>? Animated { get; set; }
        public bool Modifier { get; set; }
        public FfzUser? Owner { get; set; }
        public FfzUser? Artist { get; set; }
    }

    private sealed class FfzUser
    {
        [JsonProperty("_id")]
        public int Id { get; set; }

        public string? Name { get; set; }

        [JsonProperty("display_name")]
        public string? DisplayName { get; set; }
    }
}
