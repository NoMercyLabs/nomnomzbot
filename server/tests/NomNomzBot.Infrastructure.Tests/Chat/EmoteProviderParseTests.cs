// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves each third-party provider adapter maps its real API response to the unified <see cref="ChatEmote"/>
/// shape correctly (chat-decoration spec §3.2/§9·2): the provider tag, the scale-keyed urls (built/normalised),
/// the animated flag, and — the part the legacy bot never did — 7TV zero-width/overlay detection.
/// </summary>
public sealed class EmoteProviderParseTests
{
    // ─── BTTV ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Bttv_global_maps_animated_and_modifier_emotes()
    {
        const string json = """
            [
              { "id": "54fa8f14", "code": "WhatYouSay", "imageType": "gif", "animated": true, "modifier": false },
              { "id": "5e76d399", "code": "cvMask", "imageType": "png", "animated": false, "modifier": true }
            ]
            """;

        IReadOnlyList<ChatEmote> result = BttvEmoteProvider.ParseGlobal(json);

        result.Should().HaveCount(2);

        ChatEmote animated = result.Single(emote => emote.Code == "WhatYouSay");
        animated.Provider.Should().Be(EmoteProvider.Bttv);
        animated.Id.Should().Be("54fa8f14");
        animated.Animated.Should().BeTrue();
        animated.ZeroWidth.Should().BeFalse();
        animated.Urls["1"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/1x");
        animated.Urls["2"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/2x");
        animated.Urls["3"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/3x");

        ChatEmote modifier = result.Single(emote => emote.Code == "cvMask");
        modifier.Animated.Should().BeFalse();
        modifier.ZeroWidth.Should().BeTrue(); // a BTTV modifier emote is a zero-width overlay
    }

    [Fact]
    public void Bttv_channel_merges_channel_and_shared_emotes()
    {
        const string json = """
            {
              "channelEmotes": [ { "id": "abc", "code": "ChanEmote", "animated": false, "modifier": false } ],
              "sharedEmotes":  [ { "id": "def", "code": "SharedEmote", "animated": true,  "modifier": false } ]
            }
            """;

        IReadOnlyList<ChatEmote> result = BttvEmoteProvider.ParseChannel(json);

        result.Select(emote => emote.Code).Should().BeEquivalentTo(["ChanEmote", "SharedEmote"]);
    }

    // ─── FFZ ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Ffz_global_normalizes_urls_and_marks_modifier_zero_width()
    {
        const string json = """
            {
              "default_sets": [3],
              "sets": {
                "3": { "emoticons": [
                  { "id": 28138, "name": "ZreknarF",
                    "urls": { "1": "//cdn.frankerfacez.com/emote/28138/1", "2": "//cdn.frankerfacez.com/emote/28138/2" },
                    "modifier": false },
                  { "id": 9, "name": "Modifier", "urls": { "1": "//cdn.frankerfacez.com/emote/9/1" }, "modifier": true }
                ] }
              }
            }
            """;

        IReadOnlyList<ChatEmote> result = FfzEmoteProvider.ParseGlobal(json);

        ChatEmote emote = result.Single(item => item.Code == "ZreknarF");
        emote.Provider.Should().Be(EmoteProvider.Ffz);
        emote.Id.Should().Be("28138");
        emote.Animated.Should().BeFalse();
        emote.ZeroWidth.Should().BeFalse();
        emote.Urls["1"].Should().Be("https://cdn.frankerfacez.com/emote/28138/1"); // protocol pinned to https

        result.Single(item => item.Code == "Modifier").ZeroWidth.Should().BeTrue();
    }

    [Fact]
    public void Ffz_uses_animated_url_set_when_present()
    {
        const string json = """
            {
              "default_sets": [3],
              "sets": {
                "3": { "emoticons": [
                  { "id": 2, "name": "Anim", "urls": { "1": "//x/2/1" }, "animated": { "1": "//x/2/animated/1" }, "modifier": false }
                ] }
              }
            }
            """;

        ChatEmote emote = FfzEmoteProvider.ParseGlobal(json).Single();

        emote.Animated.Should().BeTrue();
        emote.Urls["1"].Should().Be("https://x/2/animated/1");
    }

    [Fact]
    public void Ffz_room_maps_emotes_from_all_sets()
    {
        const string json = """
            { "sets": { "12345": { "emoticons": [
                { "id": 5, "name": "RoomEmote", "urls": { "1": "//x/5/1" }, "modifier": false } ] } } }
            """;

        FfzEmoteProvider.ParseRoom(json).Single().Code.Should().Be("RoomEmote");
    }

    // ─── 7TV ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SevenTv_global_detects_animated_zero_width_and_builds_webp_urls()
    {
        const string json = """
            { "emotes": [
                { "id": "60ae", "name": "EZ", "flags": 0,
                  "data": { "animated": true, "flags": 0,
                    "host": { "url": "//cdn.7tv.app/emote/60ae",
                      "files": [ {"name":"1x.webp","format":"WEBP"}, {"name":"2x.webp","format":"WEBP"}, {"name":"1x.avif","format":"AVIF"} ] } } },
                { "id": "62f0", "name": "RainTime", "flags": 1,
                  "data": { "animated": false, "flags": 0,
                    "host": { "url": "//cdn.7tv.app/emote/62f0", "files": [ {"name":"1x.webp","format":"WEBP"} ] } } }
            ] }
            """;

        IReadOnlyList<ChatEmote> result = SevenTvEmoteProvider.ParseGlobal(json);

        ChatEmote ez = result.Single(emote => emote.Code == "EZ");
        ez.Provider.Should().Be(EmoteProvider.SevenTv);
        ez.Animated.Should().BeTrue();
        ez.ZeroWidth.Should().BeFalse();
        ez.Urls.Should().HaveCount(2); // webp only — the avif file is excluded
        ez.Urls["1"].Should().Be("https://cdn.7tv.app/emote/60ae/1x.webp");
        ez.Urls["2"].Should().Be("https://cdn.7tv.app/emote/60ae/2x.webp");

        // active-emote flag bit 0 marks a zero-width overlay
        result.Single(emote => emote.Code == "RainTime").ZeroWidth.Should().BeTrue();
    }

    [Fact]
    public void SevenTv_emote_data_zero_width_flag_marks_overlay()
    {
        const string json = """
            { "emotes": [
                { "id": "z", "name": "ZeroData", "flags": 0,
                  "data": { "animated": false, "flags": 256,
                    "host": { "url": "//cdn.7tv.app/emote/z", "files": [ {"name":"1x.webp","format":"WEBP"} ] } } }
            ] }
            """;

        SevenTvEmoteProvider.ParseGlobal(json).Single().ZeroWidth.Should().BeTrue();
    }

    [Fact]
    public void SevenTv_user_maps_the_returned_emote_set()
    {
        const string json = """
            { "emote_set": { "emotes": [
                { "id": "u", "name": "UserEmote", "flags": 0,
                  "data": { "animated": false, "flags": 0,
                    "host": { "url": "//cdn.7tv.app/emote/u", "files": [ {"name":"1x.webp","format":"WEBP"} ] } } } ] } }
            """;

        SevenTvEmoteProvider.ParseUser(json).Single().Code.Should().Be("UserEmote");
    }
}
