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
/// BetterTTV emote adapter (chat-decoration spec §3.2/§8). Global set from <c>/cached/emotes/global</c>; a
/// channel's set (channel + shared emotes) from <c>/cached/users/twitch/{broadcasterId}</c> keyed by the Twitch
/// broadcaster id. Image urls are the deterministic CDN template; <c>modifier</c> emotes are zero-width overlays.
/// </summary>
public sealed class BttvEmoteProvider : IThirdPartyEmoteProvider
{
    private const string GlobalUrl = "https://api.betterttv.net/3/cached/emotes/global";
    private const string ChannelUrl = "https://api.betterttv.net/3/cached/users/twitch/";

    private readonly IHttpClientFactory _httpClientFactory;

    public BttvEmoteProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public EmoteProvider Provider => EmoteProvider.Bttv;

    public Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(CancellationToken ct = default) =>
        EmoteHttpFetch.GetAsync(_httpClientFactory, GlobalUrl, ParseGlobal, "BTTV", ct);

    public Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
        string twitchBroadcasterId,
        string broadcasterLogin,
        CancellationToken ct = default
    ) =>
        EmoteHttpFetch.GetAsync(
            _httpClientFactory,
            $"{ChannelUrl}{twitchBroadcasterId}",
            ParseChannel,
            "BTTV",
            ct
        );

    // ─── Parsing (internal for behaviour tests) ───────────────────────────────

    internal static IReadOnlyList<ChatEmote> ParseGlobal(string json)
    {
        List<BttvEmote>? emotes = JsonConvert.DeserializeObject<List<BttvEmote>>(json);
        return Map(emotes ?? []);
    }

    internal static IReadOnlyList<ChatEmote> ParseChannel(string json)
    {
        BttvChannelEmotes? response = JsonConvert.DeserializeObject<BttvChannelEmotes>(json);
        if (response is null)
            return [];

        List<BttvEmote> all = [.. response.ChannelEmotes, .. response.SharedEmotes];
        return Map(all);
    }

    private static IReadOnlyList<ChatEmote> Map(IReadOnlyList<BttvEmote> emotes)
    {
        List<ChatEmote> result = new(emotes.Count);
        foreach (BttvEmote emote in emotes)
        {
            if (string.IsNullOrEmpty(emote.Id) || string.IsNullOrEmpty(emote.Code))
                continue;

            Dictionary<string, string> urls = new()
            {
                ["1"] = $"https://cdn.betterttv.net/emote/{emote.Id}/1x",
                ["2"] = $"https://cdn.betterttv.net/emote/{emote.Id}/2x",
                ["3"] = $"https://cdn.betterttv.net/emote/{emote.Id}/3x",
            };
            result.Add(
                new ChatEmote(
                    EmoteProvider.Bttv,
                    emote.Id,
                    emote.Code,
                    urls,
                    emote.Animated,
                    emote.Modifier
                )
            );
        }

        return result;
    }

    // ─── Provider response shapes (Newtonsoft, case-insensitive) ──────────────

    private sealed class BttvEmote
    {
        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool Animated { get; set; }
        public bool Modifier { get; set; }
    }

    private sealed class BttvChannelEmotes
    {
        public List<BttvEmote> ChannelEmotes { get; set; } = [];
        public List<BttvEmote> SharedEmotes { get; set; } = [];
    }
}
