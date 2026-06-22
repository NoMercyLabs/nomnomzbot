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
/// 7TV emote adapter (chat-decoration spec §3.2/§8/§9.2). Global set from <c>/emote-sets/global</c>; a channel's
/// set from <c>/users/twitch/{broadcasterId}</c> (the returned <c>emote_set</c>) keyed by the Twitch broadcaster
/// id. Unlike the legacy bot, this reads the data 7TV actually ships: <c>data.animated</c> ⇒ animated, and
/// zero-width/overlay from the active-emote flag (<c>1</c>) or the emote-data zero-width flag (<c>256</c>); urls
/// are built from <c>host.url</c> + <c>host.files</c> (webp per scale).
/// </summary>
public sealed class SevenTvEmoteProvider : IThirdPartyEmoteProvider
{
    private const string GlobalUrl = "https://7tv.io/v3/emote-sets/global";
    private const string UserUrl = "https://7tv.io/v3/users/twitch/";

    // 7TV zero-width markers: the active-emote flag bit 0, and the emote-data zero-width flag bit 8.
    private const int ActiveEmoteZeroWidthFlag = 1;
    private const int EmoteDataZeroWidthFlag = 256;

    private readonly IHttpClientFactory _httpClientFactory;

    public SevenTvEmoteProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmoteProvider Provider => EmoteProvider.SevenTv;

    public Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(CancellationToken ct = default) =>
        EmoteHttpFetch.GetAsync(_httpClientFactory, GlobalUrl, ParseGlobal, "7TV", ct);

    public Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
        string twitchBroadcasterId,
        string broadcasterLogin,
        CancellationToken ct = default
    ) =>
        EmoteHttpFetch.GetAsync(
            _httpClientFactory,
            $"{UserUrl}{twitchBroadcasterId}",
            ParseUser,
            "7TV",
            ct
        );

    // ─── Parsing (internal for behaviour tests) ───────────────────────────────

    internal static IReadOnlyList<ChatEmote> ParseGlobal(string json)
    {
        SevenTvEmoteSet? set = JsonConvert.DeserializeObject<SevenTvEmoteSet>(json);
        return Map(set?.Emotes ?? []);
    }

    internal static IReadOnlyList<ChatEmote> ParseUser(string json)
    {
        SevenTvUserResponse? response = JsonConvert.DeserializeObject<SevenTvUserResponse>(json);
        return Map(response?.EmoteSet?.Emotes ?? []);
    }

    private static IReadOnlyList<ChatEmote> Map(IReadOnlyList<SevenTvActiveEmote> emotes)
    {
        List<ChatEmote> result = new(emotes.Count);
        foreach (SevenTvActiveEmote emote in emotes)
        {
            if (
                string.IsNullOrEmpty(emote.Id)
                || string.IsNullOrEmpty(emote.Name)
                || emote.Data?.Host is null
            )
                continue;

            SevenTvHost host = emote.Data.Host;
            string baseUrl = host.Url.StartsWith("//", StringComparison.Ordinal)
                ? $"https:{host.Url}"
                : host.Url;

            Dictionary<string, string> urls = new();
            foreach (SevenTvFile file in host.Files)
            {
                if (!string.Equals(file.Format, "WEBP", StringComparison.OrdinalIgnoreCase))
                    continue;

                int separator = file.Name.IndexOf('x');
                if (separator <= 0)
                    continue;

                string scale = file.Name[..separator]; // "1x.webp" → "1"
                urls[scale] = $"{baseUrl}/{file.Name}";
            }

            if (urls.Count == 0)
                continue;

            bool zeroWidth =
                (emote.Flags & ActiveEmoteZeroWidthFlag) != 0
                || (emote.Data.Flags & EmoteDataZeroWidthFlag) != 0;

            result.Add(
                new ChatEmote(
                    EmoteProvider.SevenTv,
                    emote.Id,
                    emote.Name,
                    urls,
                    emote.Data.Animated,
                    zeroWidth
                )
            );
        }

        return result;
    }

    // ─── Provider response shapes (Newtonsoft) ────────────────────────────────

    private sealed class SevenTvUserResponse
    {
        [JsonProperty("emote_set")]
        public SevenTvEmoteSet? EmoteSet { get; set; }
    }

    private sealed class SevenTvEmoteSet
    {
        public List<SevenTvActiveEmote> Emotes { get; set; } = [];
    }

    private sealed class SevenTvActiveEmote
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Flags { get; set; }
        public SevenTvEmoteData? Data { get; set; }
    }

    private sealed class SevenTvEmoteData
    {
        public bool Animated { get; set; }
        public int Flags { get; set; }
        public SevenTvHost? Host { get; set; }
    }

    private sealed class SevenTvHost
    {
        public string Url { get; set; } = string.Empty;
        public List<SevenTvFile> Files { get; set; } = [];
    }

    private sealed class SevenTvFile
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
    }
}
