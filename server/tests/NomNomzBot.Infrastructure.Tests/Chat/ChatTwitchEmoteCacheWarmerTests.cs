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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Jobs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the Twitch emote warm path: the channel set lands in cache with each emote's type, tier (1000/2000/3000
/// becomes 1/2/3, only for subscriptions) and owner, the owner's login and display name are fetched ONCE per owner,
/// and a failed fetch writes nothing.
/// </summary>
public sealed class ChatTwitchEmoteCacheWarmerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000cc");

    private static TwitchChannelEmote ChannelEmote(
        string id,
        string name,
        string type,
        string tier
    ) =>
        new(
            id,
            name,
            new("u1", "u2", "u4"),
            tier,
            type,
            "set-" + id,
            ["static", "animated"],
            ["1.0"],
            ["dark"]
        );

    private static TwitchUser User(string id, string login, string display) =>
        new(id, login, display, "", "", "", "", "", 0, DateTimeOffset.UnixEpoch);

    private static ChatTwitchEmoteCacheWarmer Warmer(
        EmoteTestCache cache,
        ITwitchChatAssetsApi assets,
        ITwitchUsersApi users
    )
    {
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.ChatAssets.Returns(assets);
        helix.Users.Returns(users);
        return new(helix, cache, NullLogger<ChatTwitchEmoteCacheWarmer>.Instance);
    }

    private static ITwitchUsersApi UsersAnswering(params TwitchUser[] found)
    {
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<TwitchUser>>(found)));
        return users;
    }

    private static ITwitchChatAssetsApi ChannelAssets(params TwitchChannelEmote[] emotes)
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<TwitchChannelEmote>>(emotes)));
        return assets;
    }

    [Fact]
    public async Task Warming_a_channel_caches_each_emotes_type_tier_and_owner()
    {
        EmoteTestCache cache = new();
        ChatTwitchEmoteCacheWarmer warmer = Warmer(
            cache,
            ChannelAssets(
                ChannelEmote("300", "streamerSub", "subscriptions", "2000"),
                ChannelEmote("301", "streamerFollow", "follower", ""),
                ChannelEmote("302", "streamerBits", "bitstier", "")
            ),
            UsersAnswering(User("100", "streamer", "Streamer"))
        );

        bool warmed = await warmer.WarmChannelAsync(Broadcaster, "100");

        warmed.Should().BeTrue();
        IReadOnlyList<ChatEmote> set = (
            await cache.GetAsync<IReadOnlyList<ChatEmote>>(
                ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, "100")
            )
        )!;
        ChatEmote sub = set.Single(e => e.Id == "300");
        sub.Code.Should().Be("streamerSub");
        sub.Type.Should().Be("subscriptions");
        sub.Tier.Should().Be(2);
        sub.Owner.Should()
            .Be(
                new ChatEmoteOwner("100", "streamer", "Streamer", "https://www.twitch.tv/streamer")
            );
        set.Single(e => e.Id == "301").Type.Should().Be("follower");
        set.Single(e => e.Id == "301").Tier.Should().BeNull();
        set.Single(e => e.Id == "302").Type.Should().Be("bitstier");
    }

    [Fact]
    public async Task Warming_twice_fetches_the_owner_once()
    {
        EmoteTestCache cache = new();
        ITwitchUsersApi users = UsersAnswering(User("100", "streamer", "Streamer"));
        ChatTwitchEmoteCacheWarmer warmer = Warmer(
            cache,
            ChannelAssets(ChannelEmote("300", "streamerSub", "subscriptions", "1000")),
            users
        );

        await warmer.WarmChannelAsync(Broadcaster, "100");
        await warmer.WarmChannelAsync(Broadcaster, "100");

        await users
            .Received(1)
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        ChatEmoteOwner? owner = await cache.GetAsync<ChatEmoteOwner>(
            ChatEmoteCacheKeys.TwitchOwner("100")
        );
        owner!.Login.Should().Be("streamer");
    }

    [Fact]
    public async Task Warming_globals_marks_them_globals_with_no_owner()
    {
        EmoteTestCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchGlobalEmote>>([
                        new("25", "Kappa", new("u1", "u2", "u4"), ["static"], ["1.0"], ["dark"]),
                    ])
                )
            );

        bool warmed = await Warmer(cache, assets, UsersAnswering()).WarmGlobalAsync();

        warmed.Should().BeTrue();
        IReadOnlyList<ChatEmote> set = (
            await cache.GetAsync<IReadOnlyList<ChatEmote>>(
                ChatEmoteCacheKeys.Global(EmoteProvider.Twitch)
            )
        )!;
        set.Single().Code.Should().Be("Kappa");
        set.Single().Type.Should().Be("globals");
        set.Single().Owner.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_channel_fetch_writes_nothing()
    {
        EmoteTestCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Failure<IReadOnlyList<TwitchChannelEmote>>("boom", "TWITCH_DOWN")
                )
            );

        bool warmed = await Warmer(cache, assets, UsersAnswering())
            .WarmChannelAsync(Broadcaster, "100");

        warmed.Should().BeFalse();
        bool cached = await cache.ExistsAsync(
            ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, "100")
        );
        cached.Should().BeFalse();
    }
}
