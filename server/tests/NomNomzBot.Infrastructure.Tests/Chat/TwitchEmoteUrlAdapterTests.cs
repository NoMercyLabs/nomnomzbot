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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the Twitch emote step (chat-decoration spec §0/§4/§9·13): it fills a Twitch emote fragment's unified
/// <see cref="ChatEmote"/> from the deterministic CDN template (static vs animated, the three scales), leaves a
/// fragment the third-party step already resolved untouched, and ignores non-emote fragments.
/// </summary>
public sealed class TwitchEmoteUrlAdapterTests
{
    private static readonly TwitchEmoteUrlAdapter Adapter = new();

    private static ChatDecorationContext Context(params ChatMessageFragment[] fragments) =>
        new() { Fragments = [.. fragments] };

    [Fact]
    public async Task Builds_a_static_twitch_emote_with_all_three_scales()
    {
        ChatDecorationContext context = Context(
            new ChatMessageFragment
            {
                Type = "emote",
                Text = "Kappa",
                EmoteId = "25",
                EmoteFormats = ["static"],
            }
        );

        await Adapter.DecorateAsync(context);

        ChatEmote emote = context.Fragments.Should().ContainSingle().Subject.Emote!;
        emote.Provider.Should().Be(EmoteProvider.Twitch);
        emote.Id.Should().Be("25");
        emote.Code.Should().Be("Kappa");
        emote.Animated.Should().BeFalse();
        emote.Urls["1"].Should().Be("https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/1.0");
        emote.Urls["2"].Should().Be("https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/2.0");
        emote.Urls["3"].Should().Be("https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/3.0");
    }

    [Fact]
    public async Task Uses_the_animated_format_when_the_payload_offers_it()
    {
        ChatDecorationContext context = Context(
            new ChatMessageFragment
            {
                Type = "emote",
                Text = "PartyParrot",
                EmoteId = "99",
                EmoteFormats = ["static", "animated"],
            }
        );

        await Adapter.DecorateAsync(context);

        ChatEmote emote = context.Fragments[0].Emote!;
        emote.Animated.Should().BeTrue();
        emote
            .Urls["1"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/99/animated/dark/1.0");
    }

    [Fact]
    public async Task Leaves_a_third_party_resolved_emote_untouched()
    {
        ChatEmote sevenTv = new(
            EmoteProvider.SevenTv,
            "7tv-1",
            "PepeLaugh",
            new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
            Animated: true,
            ZeroWidth: false
        );
        ChatDecorationContext context = Context(
            new ChatMessageFragment
            {
                Type = "emote",
                Text = "PepeLaugh",
                Emote = sevenTv,
            }
        );

        await Adapter.DecorateAsync(context);

        context.Fragments[0].Emote.Should().BeSameAs(sevenTv);
    }

    [Fact]
    public async Task Ignores_non_emote_fragments()
    {
        ChatDecorationContext context = Context(
            new ChatMessageFragment { Type = "text", Text = "hello" },
            new ChatMessageFragment { Type = "mention", Text = "@stoney" }
        );

        await Adapter.DecorateAsync(context);

        context.Fragments.Should().OnlyContain(fragment => fragment.Emote == null);
    }
}
