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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the composer emote catalogue (chat-client.md §3.2): it unifies Twitch global + this channel's Twitch
/// emotes + the logged-in OPERATOR's cross-channel emotes + the warm BTTV/FFZ/7TV cache into one
/// <see cref="ChatEmote"/> list, deduped by code (channel over user over global, Twitch native over third-party);
/// builds Twitch emote urls from the same CDN template the feed uses (so animated emotes carry the animated url +
/// flag); caches the fetched Twitch sets; and degrades a failed Twitch fetch to an empty source rather than
/// failing the whole catalogue. The user-emotes source is the OPERATOR's OWN set — fetched on the operator's
/// token and cached under the OPERATOR's Twitch id (never the tenant's) — so it is correct on any channel they
/// operate and never leaks between operators.
/// </summary>
public sealed class ChatEmoteCatalogueTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000c3");
    private static readonly Guid Operator = Guid.Parse("0197b2c0-1111-7000-8000-0000000000d4");
    private const string TwitchId = "12345";

    // The operator's OWN Twitch id — deliberately DIFFERENT from the channel's, so a test can prove the
    // user-emotes source (and its cache key) rides the operator's identity, not the viewed channel's.
    private const string OperatorTwitchId = "98765";

    private static ChatEmote SevenTv(string code) =>
        new(
            EmoteProvider.SevenTv,
            $"7tv-{code}",
            code,
            new Dictionary<string, string> { ["1"] = $"https://7tv/{code}" },
            Animated: false,
            ZeroWidth: false
        );

    private static TwitchGlobalEmote Global(string name, params string[] formats) =>
        new(
            $"{name}-id",
            name,
            new("u1", "u2", "u4"),
            formats.Length == 0 ? ["static"] : formats,
            ["1.0"],
            ["dark"]
        );

    private static TwitchChannelEmote Channel(string name, string setId, params string[] formats) =>
        new(
            $"{name}-id",
            name,
            new("u1", "u2", "u4"),
            "1000",
            "subscriptions",
            setId,
            formats.Length == 0 ? ["static"] : formats,
            ["1.0"],
            ["dark"]
        );

    private static TwitchUserEmote UserEmote(string name, string setId, params string[] formats) =>
        new(
            $"{name}-id",
            name,
            "subscriptions",
            setId,
            "999",
            formats.Length == 0 ? ["static"] : formats,
            ["1.0"],
            ["dark"]
        );

    // A user emote with an EXPLICIT owning channel + emote type — for proving the cross-channel follower pass keys
    // off OwnerId (the other channels the operator has emotes from).
    private static TwitchUserEmote UserEmoteOwned(
        string name,
        string setId,
        string ownerId,
        string emoteType
    ) => new($"{name}-id", name, emoteType, setId, ownerId, ["static"], ["1.0"], ["dark"]);

    // A channel emote with an EXPLICIT emote type — the follower pass keeps only "follower", drops the rest.
    private static TwitchChannelEmote ChannelTyped(string name, string setId, string emoteType) =>
        new(
            $"{name}-id",
            name,
            new("u1", "u2", "u4"),
            "1000",
            emoteType,
            setId,
            ["static"],
            ["1.0"],
            ["dark"]
        );

    // The user-emotes source is best-effort: tests that aren't about it stub a single empty operator page so it
    // contributes nothing (and never throws on an unstubbed call).
    private static void StubNoUserEmotes(ITwitchChatAssetsApi assets) =>
        assets
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchPage<TwitchUserEmote>([], NextCursor: null, Total: 0))
            );

    // Resolves the channel Guid → its Twitch id AND the operator Guid → the operator's OWN (distinct) Twitch id,
    // the two independent lookups the catalogue makes.
    private static ITwitchIdentityResolver Identity()
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);
        identity
            .GetTwitchUserIdAsync(Operator, Arg.Any<CancellationToken>())
            .Returns(OperatorTwitchId);
        return identity;
    }

    private static ChatEmoteCatalogue Build(
        ICacheService cache,
        ITwitchChatAssetsApi assets,
        ITwitchIdentityResolver identity,
        ITwitchUsersApi? users = null,
        IThirdPartyEmoteProviderRegistry? providers = null
    )
    {
        if (users is null)
        {
            users = Substitute.For<ITwitchUsersApi>();
            users
                .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));
        }

        ChatEmoteOwnerDirectory owners = new(
            cache,
            users,
            NullLogger<ChatEmoteOwnerDirectory>.Instance
        );
        return new(
            cache,
            assets,
            identity,
            owners,
            providers ?? Substitute.For<IThirdPartyEmoteProviderRegistry>(),
            NullLogger<ChatEmoteCatalogue>.Instance
        );
    }

    private static TwitchUser User(string id, string login, string displayName) =>
        new(
            id,
            login,
            displayName,
            "",
            "affiliate",
            "",
            $"https://img/{login}.png",
            "",
            0,
            DateTimeOffset.UnixEpoch
        );

    // One batched Get Users answer for every owner the origin tests name.
    private static ITwitchUsersApi UsersApi()
    {
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    User(TwitchId, "viewedchan", "ViewedChan"),
                    User(OperatorTwitchId, "stoney_eagle", "Stoney_Eagle"),
                    User("999", "thirdchan", "ThirdChan"),
                ])
            );
        return users;
    }

    private static ITwitchChatAssetsApi OriginAssets(string channelOwner)
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa")]));
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([Channel("chanSub", "42")]));
        assets
            .GetChannelEmotesByTwitchIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        assets
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [
                            UserEmoteOwned("myOwn", "7", channelOwner, "subscriptions"),
                            UserEmoteOwned("thirdSub", "8", "999", "subscriptions"),
                            UserEmoteOwned("twitchGlobal", "9", "twitch", "globals"),
                        ],
                        NextCursor: null,
                        Total: 3
                    )
                )
            );
        return assets;
    }

    [Fact]
    public async Task Viewing_another_channel_tags_channel_own_other_and_global_with_owner_names()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, TwitchId),
            [SevenTv("catJAM")]
        );
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [SevenTv("peepoHappy")]
        );
        ITwitchUsersApi users = UsersApi();

        Result<IReadOnlyList<ChatEmote>> result = await Build(
                cache,
                OriginAssets(OperatorTwitchId),
                Identity(),
                users
            )
            .GetForChannelAsync(Broadcaster, Operator);

        IReadOnlyList<ChatEmote> emotes = result.Value;

        // The viewed channel's own Twitch emote: "channel", named after that channel, provider id kept.
        ChatEmote sub = emotes.Single(e => e.Code == "chanSub");
        sub.Origin.Should().Be(ChatEmoteOrigin.Channel);
        sub.Id.Should().Be("chanSub-id");
        sub.OwnerChannel!.Id.Should().Be(TwitchId);
        sub.OwnerChannel.Login.Should().Be("viewedchan");
        sub.OwnerChannel.DisplayName.Should().Be("ViewedChan");
        sub.OwnerChannel.AvatarUrl.Should().Be("https://img/viewedchan.png");

        // A 7TV channel set belongs to the viewed channel too; the uploader stays untouched.
        ChatEmote seventv = emotes.Single(e => e.Code == "catJAM");
        seventv.Origin.Should().Be(ChatEmoteOrigin.Channel);
        seventv.OwnerChannel!.DisplayName.Should().Be("ViewedChan");
        seventv.OwnerChannel.AvatarUrl.Should().Be("https://img/viewedchan.png");

        // The operator's own emote is "own"; a third channel's sub emote is "other" with that owner's name.
        ChatEmote mine = emotes.Single(e => e.Code == "myOwn");
        mine.Origin.Should().Be(ChatEmoteOrigin.Own);
        mine.OwnerChannel!.DisplayName.Should().Be("Stoney_Eagle");
        mine.OwnerChannel.AvatarUrl.Should().Be("https://img/stoney_eagle.png");
        ChatEmote other = emotes.Single(e => e.Code == "thirdSub");
        other.Origin.Should().Be(ChatEmoteOrigin.Other);
        other.OwnerChannel!.Login.Should().Be("thirdchan");

        // Globals carry no owner channel.
        foreach (string code in new[] { "Kappa", "peepoHappy", "twitchGlobal" })
        {
            ChatEmote global = emotes.Single(e => e.Code == code);
            global.Origin.Should().Be(ChatEmoteOrigin.Global);
            global.OwnerChannel.Should().BeNull();
        }

        // Every owner is named in ONE batched Get Users call, never one per emote.
        await users
            .Received(1)
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Viewing_your_own_channel_tags_your_twitch_emotes_own()
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(OperatorTwitchId);
        identity
            .GetTwitchUserIdAsync(Operator, Arg.Any<CancellationToken>())
            .Returns(OperatorTwitchId);
        ITwitchChatAssetsApi assets = OriginAssets(OperatorTwitchId);
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([Channel("ownSub", "42")]));

        Result<IReadOnlyList<ChatEmote>> result = await Build(
                new FakeCache(),
                assets,
                identity,
                UsersApi()
            )
            .GetForChannelAsync(Broadcaster, Operator);

        foreach (string code in new[] { "ownSub", "myOwn" })
        {
            ChatEmote emote = result.Value.Single(e => e.Code == code);
            emote.Origin.Should().Be(ChatEmoteOrigin.Own);
            emote.OwnerChannel!.DisplayName.Should().Be("Stoney_Eagle");
        }
    }

    [Fact]
    public async Task The_bot_catalogue_never_has_own_emotes()
    {
        Result<IReadOnlyList<ChatEmote>> result = await Build(
                new FakeCache(),
                OriginAssets(OperatorTwitchId),
                Identity(),
                UsersApi()
            )
            .GetForChannelAsync(Broadcaster, Operator, ChatEmoteSender.Bot);

        result.Value.Should().NotContain(e => e.Origin == ChatEmoteOrigin.Own);
        result.Value.Should().OnlyContain(e => e.Origin == ChatEmoteOrigin.Global);
    }

    [Fact]
    public async Task Assembles_all_sources_deduped_channel_over_global_and_twitch_over_third_party()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, TwitchId),
            [SevenTv("catJAM"), SevenTv("Dup")]
        );
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [SevenTv("peepoHappy")]
        );

        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa"), Global("Dup")])
            );
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                    Channel("stoneyWave", "42", "static", "animated"),
                ])
            );
        StubNoUserEmotes(assets);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        IReadOnlyList<ChatEmote> emotes = result.Value;

        // Every source contributes; deduped by code.
        emotes
            .Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["stoneyWave", "Kappa", "Dup", "catJAM", "peepoHappy"]);

        // The "Dup" collision resolves to the Twitch-native emote (added before third-party).
        emotes.Single(e => e.Code == "Dup").Provider.Should().Be(EmoteProvider.Twitch);

        // The animated channel emote carries the animated CDN url + flag + set id — same template the feed uses.
        ChatEmote wave = emotes.Single(e => e.Code == "stoneyWave");
        wave.Animated.Should().BeTrue();
        wave.SetId.Should().Be("42");
        wave.Urls["2"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/stoneyWave-id/animated/dark/2.0");
    }

    [Fact]
    public async Task Bot_sender_excludes_operator_and_channel_twitch_emotes_keeping_global_and_third_party()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, TwitchId),
            [SevenTv("catJAM")]
        );
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [SevenTv("peepoHappy")]
        );

        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa")]));
        // The channel's subscriber emote and the operator's personal emote are available — the bot must get NEITHER.
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([Channel("stoneySub", "42")])
            );
        assets
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [UserEmote("myPersonalSub", "777")],
                        NextCursor: null,
                        Total: 1
                    )
                )
            );

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator, ChatEmoteSender.Bot);

        result.IsSuccess.Should().BeTrue();
        // The bot can send Twitch global + the third-party text codes — nothing the bot cannot use.
        result
            .Value.Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["Kappa", "catJAM", "peepoHappy"]);
        result.Value.Should().NotContain(e => e.Code == "stoneySub");
        result.Value.Should().NotContain(e => e.Code == "myPersonalSub");

        // The bot path skips the subscriber-gated / operator-scoped Twitch calls entirely — not merely filters them out.
        await assets
            .DidNotReceive()
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>());
        await assets
            .DidNotReceive()
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_twitch_fetch_failure_omits_that_source_but_still_returns_third_party()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [SevenTv("peepoHappy")]
        );

        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchGlobalEmote>>(
                    "twitch down",
                    TwitchErrorCodes.NotFound
                )
            );
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchChannelEmote>>(
                    "twitch down",
                    TwitchErrorCodes.NotFound
                )
            );
        StubNoUserEmotes(assets);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(e => e.Code).Should().ContainSingle().Which.Should().Be("peepoHappy");
    }

    [Fact]
    public async Task Caches_the_fetched_twitch_sets_so_a_second_call_does_not_refetch()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa")]));
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        StubNoUserEmotes(assets);

        ChatEmoteCatalogue catalogue = Build(cache, assets, Identity());
        await catalogue.GetForChannelAsync(Broadcaster, Operator);
        await catalogue.GetForChannelAsync(Broadcaster, Operator);

        // The global set is fetched once, then served from cache on the second call.
        await assets.Received(1).GetGlobalEmotesAsync(Arg.Any<CancellationToken>());
        // The operator's user set too — keyed to the operator, cached after the first call.
        await assets
            .Received(1)
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Includes_the_operators_cross_channel_emotes_as_the_operator_keyed_to_the_operator()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([]));
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        // The operator's cross-channel emotes below are owned by another channel (999), so the follower pass fetches
        // that channel; it has no follower emotes to add here — this test is about the cursor walk, not the pass.
        assets
            .GetChannelEmotesByTwitchIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));

        // The fetch must ride the OPERATOR's Guid, with the CHANNEL's Twitch id as the optional broadcaster_id.
        // Page 1 hands back a cursor; page 2 exhausts it — the walk must follow the cursor.
        assets
            .GetUserEmotesAsOperatorAsync(Operator, TwitchId, null, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [UserEmote("subFromFriendA", "301")],
                        NextCursor: "page2",
                        Total: 0
                    )
                )
            );
        assets
            .GetUserEmotesAsOperatorAsync(Operator, TwitchId, "page2", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [UserEmote("subFromFriendB", "302", "animated")],
                        NextCursor: null,
                        Total: 0
                    )
                )
            );

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        IReadOnlyList<ChatEmote> emotes = result.Value;

        // Both pages of the operator's cross-channel emotes reach the catalogue.
        emotes.Select(e => e.Code).Should().Contain(["subFromFriendA", "subFromFriendB"]);

        // Mapped to the Twitch-native ChatEmote shape: provider, set id, animated flag + the animated CDN url.
        ChatEmote b = emotes.Single(e => e.Code == "subFromFriendB");
        b.Provider.Should().Be(EmoteProvider.Twitch);
        b.Animated.Should().BeTrue();
        b.SetId.Should().Be("302");
        b.Urls["1"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/subFromFriendB-id/animated/dark/1.0");

        // The fetch went out AS THE OPERATOR, carrying the CHANNEL's Twitch id as the optional broadcaster_id —
        // and the second page was requested with the returned cursor.
        await assets
            .Received(1)
            .GetUserEmotesAsOperatorAsync(Operator, TwitchId, null, Arg.Any<CancellationToken>());
        await assets
            .Received(1)
            .GetUserEmotesAsOperatorAsync(
                Operator,
                TwitchId,
                "page2",
                Arg.Any<CancellationToken>()
            );

        // The assembled set is cached against the OPERATOR's Twitch id (98765), NOT the viewed channel's (12345),
        // so operator A's subscription emotes can never leak into operator B's composer on the same channel.
        IReadOnlyList<ChatEmote>? operatorCached = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.TwitchUser(OperatorTwitchId)
        );
        operatorCached.Should().NotBeNull();
        operatorCached
            .Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["subFromFriendA", "subFromFriendB"]);

        // Nothing is cached under the CHANNEL's id — proving the key is the operator's, not the tenant's.
        (await cache.GetAsync<IReadOnlyList<ChatEmote>>(ChatEmoteCacheKeys.TwitchUser(TwitchId)))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task Back_fills_cross_channel_follower_emotes_but_not_the_other_channels_sub_emotes()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([]));
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));

        // Base Get User Emotes: a sub emote from the CURRENT channel (owner 12345) and a sub emote from ANOTHER
        // channel the operator follows (owner 111). Twitch returns the operator's sub emotes for every channel, but
        // a channel's FOLLOWER emotes only for the one passed as broadcaster_id — so channel 111's follower emote is
        // absent from this page and must be back-filled per channel.
        assets
            .GetUserEmotesAsOperatorAsync(Operator, TwitchId, null, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [
                            UserEmoteOwned("homeSub", "10", TwitchId, "subscriptions"),
                            UserEmoteOwned("verosSub", "11", "111", "subscriptions"),
                        ],
                        NextCursor: null,
                        Total: 0
                    )
                )
            );

        // Get Channel Emotes for the OTHER channel (111): a follower emote (the operator's via the follower reward —
        // must be added) and a sub emote (NOT the operator's to use — must be dropped).
        assets
            .GetChannelEmotesByTwitchIdAsync("111", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                    ChannelTyped("verosWaving", "12", "follower"),
                    ChannelTyped("verosOtherSub", "13", "subscriptions"),
                ])
            );

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        List<string> codes = [.. result.Value.Select(e => e.Code)];

        // The operator's own sub emotes (home + cross-channel) still come through the base walk.
        codes.Should().Contain(["homeSub", "verosSub"]);
        // The cross-channel FOLLOWER emote is back-filled from the other channel...
        codes.Should().Contain("verosWaving");
        // ...but the other channel's SUB emote (which the operator does not own) is NOT.
        codes.Should().NotContain("verosOtherSub");

        // The back-filled follower emote is a Twitch-native ChatEmote carrying that channel's set id.
        ChatEmote waving = result.Value.Single(e => e.Code == "verosWaving");
        waving.Provider.Should().Be(EmoteProvider.Twitch);
        waving.SetId.Should().Be("12");

        // The follower pass fetched the OTHER channel exactly once...
        await assets
            .Received(1)
            .GetChannelEmotesByTwitchIdAsync("111", Arg.Any<CancellationToken>());
        // ...and NEVER the current channel — its follower emotes already arrived through broadcaster_id.
        await assets
            .DidNotReceive()
            .GetChannelEmotesByTwitchIdAsync(TwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Operator_with_no_linked_twitch_identity_omits_user_emotes_but_returns_the_rest()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [SevenTv("peepoHappy")]
        );

        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa")]));
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        // If the catalogue ever reached for the operator's emotes despite no identity, this would inject one.
        assets
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [UserEmote("shouldNotAppear", "500")],
                        NextCursor: null,
                        Total: 0
                    )
                )
            );

        // The channel resolves, but the operator has NO linked Twitch identity (GetTwitchUserIdAsync → null).
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);
        identity
            .GetTwitchUserIdAsync(Operator, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, identity)
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        // The rest of the catalogue still returns; the operator's user-emotes source is simply absent.
        result.Value.Select(e => e.Code).Should().BeEquivalentTo(["Kappa", "peepoHappy"]);
        result.Value.Select(e => e.Code).Should().NotContain("shouldNotAppear");

        // Never even attempted the fetch — there is no operator token to read as.
        await assets
            .DidNotReceive()
            .GetUserEmotesAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Dedup_is_case_sensitive_so_differently_cased_codes_all_survive()
    {
        FakeCache cache = new();
        // Two DISTINCT 7TV codes differing only in case, plus a genuine same-case collision resolved below.
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, TwitchId),
            [SevenTv("Pog"), SevenTv("POG")]
        );

        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        // A Twitch-native "Pog" — exact-case match of the 7TV "Pog": the higher-precedence Twitch one must win it.
        assets
            .GetChannelEmotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([Channel("Pog", "7")]));
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([]));
        StubNoUserEmotes(assets);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, Identity())
            .GetForChannelAsync(Broadcaster, Operator);

        result.IsSuccess.Should().BeTrue();
        IReadOnlyList<ChatEmote> emotes = result.Value;

        // "Pog" and "POG" are three-distinct-emote territory — both codes survive the case-sensitive dedup.
        emotes
            .Where(e => string.Equals(e.Code, "pog", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["Pog", "POG"]);

        // The genuine same-case "Pog" collision keeps the higher-precedence Twitch-native emote over the 7TV one.
        emotes.Single(e => e.Code == "Pog").Provider.Should().Be(EmoteProvider.Twitch);
        // The differently-cased "POG" is left untouched — still the 7TV emote.
        emotes.Single(e => e.Code == "POG").Provider.Should().Be(EmoteProvider.SevenTv);
    }

    private static readonly Guid Person = Guid.Parse("0197b2c0-2222-7000-8000-0000000000e5");
    private const string PersonTwitchId = "555";

    private static IThirdPartyEmoteProviderRegistry Registry(
        EmoteProvider provider,
        Result<IReadOnlyList<ChatEmote>> channelSet
    )
    {
        IThirdPartyEmoteProvider adapter = Substitute.For<IThirdPartyEmoteProvider>();
        adapter.Provider.Returns(provider);
        adapter
            .GetChannelAsync(PersonTwitchId, "personchan", Arg.Any<CancellationToken>())
            .Returns(channelSet);
        IThirdPartyEmoteProviderRegistry registry =
            Substitute.For<IThirdPartyEmoteProviderRegistry>();
        registry.Get(Arg.Any<EmoteProvider>()).Returns((IThirdPartyEmoteProvider?)null);
        registry.Get(provider).Returns(adapter);
        return registry;
    }

    private static ITwitchUsersApi PersonUsers()
    {
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    User(PersonTwitchId, "personchan", "PersonChan"),
                ])
            );
        return users;
    }

    private static ITwitchIdentityResolver PersonIdentity()
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity.GetTwitchUserIdAsync(Person, Arg.Any<CancellationToken>()).Returns(PersonTwitchId);
        return identity;
    }

    [Fact]
    public async Task GetForTwitchChannelAsync_returns_an_off_bot_channels_own_emotes_by_its_twitch_id()
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        StubNoUserEmotes(assets);
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([Global("Kappa")]));
        assets
            .GetChannelEmotesByTwitchIdAsync(PersonTwitchId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                    ChannelTyped("offBotSub", "40", "subscriptions"),
                ])
            );
        IThirdPartyEmoteProviderRegistry registry = Registry(
            EmoteProvider.SevenTv,
            Result.Success<IReadOnlyList<ChatEmote>>([SevenTv("offBotJAM")])
        );

        Result<IReadOnlyList<ChatEmote>> result = await Build(
                new FakeCache(),
                assets,
                Identity(),
                PersonUsers(),
                registry
            )
            .GetForTwitchChannelAsync(PersonTwitchId, Operator);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(e => e.Code).Should().BeEquivalentTo("offBotSub", "offBotJAM", "Kappa");
        ChatEmote sub = result.Value.Single(e => e.Code == "offBotSub");
        sub.Origin.Should().Be(ChatEmoteOrigin.Channel);
        sub.OwnerChannel!.Login.Should().Be("personchan");
        result.Value.Single(e => e.Code == "Kappa").Origin.Should().Be(ChatEmoteOrigin.Global);
        await assets
            .Received(1)
            .GetUserEmotesAsOperatorAsync(
                Operator,
                PersonTwitchId,
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await assets
            .DidNotReceive()
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForPersonAsync_returns_the_persons_twitch_and_third_party_channel_emotes_as_their_own()
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesByTwitchIdAsync(PersonTwitchId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEmote>>([
                    ChannelTyped("personSub", "30", "subscriptions"),
                    ChannelTyped("personFollow", "31", "follower"),
                ])
            );
        IThirdPartyEmoteProviderRegistry registry = Registry(
            EmoteProvider.SevenTv,
            Result.Success<IReadOnlyList<ChatEmote>>([SevenTv("personJAM")])
        );

        Result<IReadOnlyList<ChatEmote>> result = await Build(
                new FakeCache(),
                assets,
                PersonIdentity(),
                PersonUsers(),
                registry
            )
            .GetForPersonAsync(Person);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Select(e => e.Code)
            .Should()
            .BeEquivalentTo("personSub", "personFollow", "personJAM");
        result.Value.Should().OnlyContain(e => e.Origin == ChatEmoteOrigin.Channel);
        result
            .Value.Should()
            .OnlyContain(e =>
                e.OwnerChannel != null
                && e.OwnerChannel.Id == PersonTwitchId
                && e.OwnerChannel.Login == "personchan"
            );
        ChatEmote sub = result.Value.Single(e => e.Code == "personSub");
        sub.Type.Should().Be("subscriptions");
        sub.Tier.Should().Be(1);
        result.Value.Single(e => e.Code == "personFollow").Type.Should().Be("follower");
    }

    [Fact]
    public async Task GetForPersonAsync_caches_the_fetched_third_party_set_under_the_warmer_key()
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesByTwitchIdAsync(PersonTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        FakeCache cache = new();

        await Build(
                cache,
                assets,
                PersonIdentity(),
                PersonUsers(),
                Registry(
                    EmoteProvider.SevenTv,
                    Result.Success<IReadOnlyList<ChatEmote>>([SevenTv("personJAM")])
                )
            )
            .GetForPersonAsync(Person);

        IReadOnlyList<ChatEmote>? cached = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, PersonTwitchId)
        );
        cached.Should().ContainSingle().Which.Code.Should().Be("personJAM");
    }

    [Fact]
    public async Task GetForPersonAsync_returns_an_empty_list_for_a_person_with_no_twitch_identity()
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity.GetTwitchUserIdAsync(Person, Arg.Any<CancellationToken>()).Returns((string?)null);
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();

        Result<IReadOnlyList<ChatEmote>> result = await Build(new FakeCache(), assets, identity)
            .GetForPersonAsync(Person);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await assets.DidNotReceiveWithAnyArgs().GetChannelEmotesByTwitchIdAsync(default!, default);
    }

    [Fact]
    public async Task GetForPersonAsync_returns_an_empty_list_when_every_source_fails_or_is_empty()
    {
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelEmotesByTwitchIdAsync(PersonTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));
        IThirdPartyEmoteProviderRegistry registry = Registry(
            EmoteProvider.SevenTv,
            Result.Failure<IReadOnlyList<ChatEmote>>("PROVIDER_DOWN", "down")
        );

        Result<IReadOnlyList<ChatEmote>> result = await Build(
                new FakeCache(),
                assets,
                PersonIdentity(),
                PersonUsers(),
                registry
            )
            .GetForPersonAsync(Person);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // Turbo emotes carry the owner id "twitch". Twitch rejects a whole Get Users batch for one non-numeric id,
    // which left 322 of 392 channel emotes unnamed in the picker (live data, 2026-10-14).
    [Fact]
    public async Task OwnerDirectory_NamesEveryRealOwner_WhenATurboOwnerSharesTheBatch()
    {
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<IReadOnlyList<string>>().All(id => id.All(char.IsAsciiDigit))
                    ? Result.Success<IReadOnlyList<TwitchUser>>([
                        User("999", "thirdchan", "ThirdChan"),
                    ])
                    : Result.Failure<IReadOnlyList<TwitchUser>>("Invalid id", "BAD_REQUEST")
            );
        ChatEmoteOwnerDirectory directory = new(
            new FakeCache(),
            users,
            NullLogger<ChatEmoteOwnerDirectory>.Instance
        );

        IReadOnlyDictionary<string, ChatEmoteOwner> owners = await directory.ResolveAsync([
            "999",
            "twitch",
        ]);

        owners.Should().ContainKey("999");
        owners["999"].DisplayName.Should().Be("ThirdChan");
        owners.Should().NotContainKey("twitch");
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }
}
