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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves a Twitch emote in chat carries its type, tier and owner from the cached emote sets (cache only, no Helix
/// call): a subscription emote, a follower emote, a global emote with no owner, and an emote from a channel the
/// cache has no set for, where only the owner id (and a cached owner name) can be known.
/// </summary>
public sealed class TwitchEmoteUrlAdapterTests
{
    private const string Channel = "100";

    private static readonly ChatEmoteOwner ChannelOwner = new(
        "100",
        "streamer",
        "Streamer",
        "https://www.twitch.tv/streamer"
    );

    private static ChatEmote Cached(
        string id,
        string code,
        string type,
        int? tier,
        ChatEmoteOwner? owner
    ) =>
        new(
            EmoteProvider.Twitch,
            id,
            code,
            new Dictionary<string, string>(),
            Animated: false,
            ZeroWidth: false,
            SetId: "set-1",
            OwnerId: owner?.Id,
            Formats: ["static"],
            Type: type,
            Tier: tier,
            Owner: owner
        );

    private static async Task<EmoteTestCache> SeededCacheAsync()
    {
        EmoteTestCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, Channel),
            [
                Cached("300", "streamerSub", "subscriptions", 1, ChannelOwner),
                Cached("301", "streamerFollow", "follower", null, ChannelOwner),
            ]
        );
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.Twitch),
            [Cached("25", "Kappa", "globals", null, null)]
        );
        return cache;
    }

    private static async Task<ChatEmote> DecorateAsync(
        EmoteTestCache cache,
        string emoteId,
        string text,
        string? ownerId
    )
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = text,
            EmoteId = emoteId,
            EmoteOwnerId = ownerId,
            EmoteFormats = ["static"],
        };
        ChatDecorationContext context = new()
        {
            TwitchBroadcasterId = Channel,
            Fragments = [fragment],
        };

        await new TwitchEmoteUrlAdapter(cache).DecorateAsync(context);

        return context.Fragments[0].Emote!;
    }

    [Fact]
    public async Task A_subscription_emote_carries_its_type_tier_and_owning_channel()
    {
        ChatEmote emote = await DecorateAsync(
            await SeededCacheAsync(),
            "300",
            "streamerSub",
            "100"
        );

        emote.Code.Should().Be("streamerSub");
        emote.Type.Should().Be("subscriptions");
        emote.Tier.Should().Be(1);
        emote.Owner.Should().Be(ChannelOwner);
    }

    [Fact]
    public async Task A_follower_emote_has_no_tier()
    {
        ChatEmote emote = await DecorateAsync(
            await SeededCacheAsync(),
            "301",
            "streamerFollow",
            "100"
        );

        emote.Type.Should().Be("follower");
        emote.Tier.Should().BeNull();
        emote.Owner!.Login.Should().Be("streamer");
    }

    [Fact]
    public async Task A_global_emote_has_a_globals_type_and_no_owner()
    {
        ChatEmote emote = await DecorateAsync(await SeededCacheAsync(), "25", "Kappa", null);

        emote.Code.Should().Be("Kappa");
        emote.Type.Should().Be("globals");
        emote.Tier.Should().BeNull();
        emote.Owner.Should().BeNull();
    }

    [Fact]
    public async Task An_emote_from_another_channel_takes_its_owner_name_from_the_owner_cache()
    {
        EmoteTestCache cache = await SeededCacheAsync();
        ChatEmoteOwner other = new("555", "other", "Other", "https://www.twitch.tv/other");
        await cache.SetAsync(ChatEmoteCacheKeys.TwitchOwner("555"), other);

        ChatEmote emote = await DecorateAsync(cache, "900", "otherSub", "555");

        emote.Type.Should().BeNull();
        emote.Owner.Should().Be(other);
    }

    [Fact]
    public async Task An_emote_the_cache_does_not_know_keeps_only_the_owner_id()
    {
        ChatEmote emote = await DecorateAsync(await SeededCacheAsync(), "900", "otherSub", "555");

        emote.Type.Should().BeNull();
        emote.Tier.Should().BeNull();
        emote.Owner.Should().Be(new ChatEmoteOwner("555", null, null));
    }
}
