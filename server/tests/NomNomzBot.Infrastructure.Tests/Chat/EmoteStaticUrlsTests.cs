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

using FluentAssertions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves every emote source fills <see cref="ChatEmote.StaticUrls"/> with the still image of an animated emote, at
/// the same scales as <see cref="ChatEmote.Urls"/>, so the dashboard can honor reduced motion. A non-animated emote
/// repeats its normal urls. Each url shape was fetched live on 2026-10-14 (see the slice report).
/// </summary>
public sealed class EmoteStaticUrlsTests
{
    private static readonly TwitchEmoteImages Images = new("a", "b", "c");

    [Fact]
    public void Twitch_animated_emote_gets_the_static_format_urls_beside_the_animated_ones()
    {
        TwitchGlobalEmote emote = new(
            "emotesv2_abc",
            "Hype",
            Images,
            ["static", "animated"],
            ["1.0", "2.0", "3.0"],
            ["dark"]
        );

        ChatEmote result = TwitchEmoteMapper.FromGlobal(emote);

        result.Animated.Should().BeTrue();
        result
            .Urls["2"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/emotesv2_abc/animated/dark/2.0");
        result.StaticUrls.Should().NotBeNull();
        result.StaticUrls!.Keys.Should().BeEquivalentTo(result.Urls.Keys);
        result
            .StaticUrls["1"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/emotesv2_abc/static/dark/1.0");
        result
            .StaticUrls["3"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/emotesv2_abc/static/dark/3.0");
    }

    [Fact]
    public void Twitch_static_emote_repeats_its_urls_as_the_static_urls()
    {
        TwitchGlobalEmote emote = new("25", "Kappa", Images, ["static"], ["1.0"], ["dark"]);

        ChatEmote result = TwitchEmoteMapper.FromGlobal(emote);

        result.Animated.Should().BeFalse();
        result.StaticUrls.Should().BeEquivalentTo(result.Urls);
    }

    [Fact]
    public void SevenTv_animated_emote_gets_the_static_webp_for_every_scale()
    {
        const string json = """
            { "emotes": [
                { "id": "60ae", "name": "EZ", "flags": 0,
                  "data": { "animated": true, "flags": 0,
                    "host": { "url": "//cdn.7tv.app/emote/60ae",
                      "files": [ {"name":"1x.webp","format":"WEBP"}, {"name":"2x.webp","format":"WEBP"}, {"name":"1x.gif","format":"GIF"} ] } } }
            ] }
            """;

        ChatEmote emote = SevenTvEmoteProvider.ParseGlobal(json).Single();

        emote.Urls["2"].Should().Be("https://cdn.7tv.app/emote/60ae/2x.webp");
        emote.StaticUrls.Should().NotBeNull();
        emote.StaticUrls!.Should().HaveCount(2);
        emote.StaticUrls["1"].Should().Be("https://cdn.7tv.app/emote/60ae/1x_static.webp");
        emote.StaticUrls["2"].Should().Be("https://cdn.7tv.app/emote/60ae/2x_static.webp");
    }

    [Fact]
    public void SevenTv_static_emote_repeats_its_urls_as_the_static_urls()
    {
        const string json = """
            { "emotes": [
                { "id": "u", "name": "Still", "flags": 0,
                  "data": { "animated": false, "flags": 0,
                    "host": { "url": "//cdn.7tv.app/emote/u", "files": [ {"name":"1x.webp","format":"WEBP"} ] } } }
            ] }
            """;

        ChatEmote emote = SevenTvEmoteProvider.ParseGlobal(json).Single();

        emote.StaticUrls.Should().BeEquivalentTo(emote.Urls);
    }

    [Fact]
    public void Bttv_animated_emote_gets_the_png_urls_as_the_static_urls()
    {
        const string json = """
            [ { "id": "54fa8f14", "code": "WhatYouSay", "imageType": "gif", "animated": true, "modifier": false } ]
            """;

        ChatEmote emote = BttvEmoteProvider.ParseGlobal(json).Single();

        emote.Urls["2"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/2x");
        emote.StaticUrls.Should().NotBeNull();
        emote.StaticUrls!.Keys.Should().BeEquivalentTo(emote.Urls.Keys);
        emote.StaticUrls["1"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/1x.png");
        emote.StaticUrls["3"].Should().Be("https://cdn.betterttv.net/emote/54fa8f14/3x.png");
    }

    [Fact]
    public void Bttv_static_emote_repeats_its_urls_as_the_static_urls()
    {
        const string json = """
            [ { "id": "5e76d399", "code": "cvMask", "imageType": "png", "animated": false, "modifier": true } ]
            """;

        ChatEmote emote = BttvEmoteProvider.ParseGlobal(json).Single();

        emote.StaticUrls.Should().BeEquivalentTo(emote.Urls);
    }

    [Fact]
    public void Ffz_animated_emote_uses_the_plain_urls_as_the_static_urls()
    {
        const string json = """
            { "default_sets": [3], "sets": { "3": { "emoticons": [
                { "id": 2, "name": "Anim", "urls": { "1": "//x/2/1", "2": "//x/2/2" },
                  "animated": { "1": "//x/2/animated/1", "2": "//x/2/animated/2" }, "modifier": false }
            ] } } }
            """;

        ChatEmote emote = FfzEmoteProvider.ParseGlobal(json).Single();

        emote.Animated.Should().BeTrue();
        emote.Urls["1"].Should().Be("https://x/2/animated/1");
        emote.StaticUrls.Should().NotBeNull();
        emote.StaticUrls!.Should().HaveCount(2);
        emote.StaticUrls["1"].Should().Be("https://x/2/1");
        emote.StaticUrls["2"].Should().Be("https://x/2/2");
    }

    [Fact]
    public void Ffz_static_emote_repeats_its_urls_as_the_static_urls()
    {
        const string json = """
            { "default_sets": [3], "sets": { "3": { "emoticons": [
                { "id": 5, "name": "Still", "urls": { "1": "//x/5/1" }, "modifier": false }
            ] } } }
            """;

        ChatEmote emote = FfzEmoteProvider.ParseGlobal(json).Single();

        emote.StaticUrls.Should().BeEquivalentTo(emote.Urls);
    }
}
