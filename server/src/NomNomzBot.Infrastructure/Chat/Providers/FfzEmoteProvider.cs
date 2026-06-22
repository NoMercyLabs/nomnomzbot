// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

        return Map(emoticons);
    }

    internal static IReadOnlyList<ChatEmote> ParseRoom(string json)
    {
        FfzRoomResponse? response = JsonConvert.DeserializeObject<FfzRoomResponse>(json);
        if (response is null)
            return [];

        List<FfzEmoticon> emoticons = [];
        foreach (FfzSet set in response.Sets.Values)
            emoticons.AddRange(set.Emoticons);

        return Map(emoticons);
    }

    private static IReadOnlyList<ChatEmote> Map(IReadOnlyList<FfzEmoticon> emoticons)
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
            Dictionary<string, string> urls = new(source.Count);
            foreach (KeyValuePair<string, string> entry in source)
                urls[entry.Key] = Normalize(entry.Value);

            result.Add(
                new ChatEmote(
                    EmoteProvider.Ffz,
                    emoticon.Id.ToString(),
                    emoticon.Name,
                    urls,
                    animated,
                    emoticon.Modifier
                )
            );
        }

        return result;
    }

    // FFZ ships protocol-relative urls ("//cdn.frankerfacez.com/..."); pin them to https.
    private static string Normalize(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;

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
    }
}
