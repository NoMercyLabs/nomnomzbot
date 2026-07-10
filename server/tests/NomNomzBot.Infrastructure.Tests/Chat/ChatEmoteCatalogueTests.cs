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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the composer emote catalogue (chat-client.md §3.2): it unifies Twitch global + this channel's Twitch
/// emotes + the warm BTTV/FFZ/7TV cache into one <see cref="ChatEmote"/> list, deduped by code (channel over
/// global, Twitch native over third-party); builds Twitch emote urls from the same CDN template the feed uses
/// (so animated emotes carry the animated url + flag); caches the fetched Twitch sets; and degrades a failed
/// Twitch fetch to an empty source rather than failing the whole catalogue.
/// </summary>
public sealed class ChatEmoteCatalogueTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000c3");
    private const string TwitchId = "12345";

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
            new TwitchEmoteImages("u1", "u2", "u4"),
            formats.Length == 0 ? ["static"] : formats,
            ["1.0"],
            ["dark"]
        );

    private static TwitchChannelEmote Channel(string name, string setId, params string[] formats) =>
        new(
            $"{name}-id",
            name,
            new TwitchEmoteImages("u1", "u2", "u4"),
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

    // The user-emotes source is best-effort: tests that aren't about it stub a single empty page so it
    // contributes nothing (and never throws on an unstubbed call).
    private static void StubNoUserEmotes(ITwitchChatAssetsApi assets) =>
        assets
            .GetUserEmotesAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new TwitchPage<TwitchUserEmote>([], NextCursor: null, Total: 0))
            );

    private static ChatEmoteCatalogue Build(
        ICacheService cache,
        ITwitchChatAssetsApi assets,
        ITwitchIdentityResolver identity
    ) => new(cache, assets, identity, NullLogger<ChatEmoteCatalogue>.Instance);

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

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, identity)
            .GetForChannelAsync(Broadcaster);

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

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, identity)
            .GetForChannelAsync(Broadcaster);

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

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);

        ChatEmoteCatalogue catalogue = Build(cache, assets, identity);
        await catalogue.GetForChannelAsync(Broadcaster);
        await catalogue.GetForChannelAsync(Broadcaster);

        // The global set is fetched once, then served from cache on the second call.
        await assets.Received(1).GetGlobalEmotesAsync(Arg.Any<CancellationToken>());
        // The user set too — keyed to the resolved user, cached after the first call.
        await assets
            .Received(1)
            .GetUserEmotesAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Includes_the_users_cross_channel_emotes_following_the_cursor_across_pages()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalEmotesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchGlobalEmote>>([]));
        assets
            .GetChannelEmotesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEmote>>([]));

        // Page 1 hands back a cursor; page 2 exhausts it (NextCursor null). The walk must follow the cursor.
        assets
            .GetUserEmotesAsync(Broadcaster, null, Arg.Any<CancellationToken>())
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
            .GetUserEmotesAsync(Broadcaster, "page2", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUserEmote>(
                        [UserEmote("subFromFriendB", "302", "animated")],
                        NextCursor: null,
                        Total: 0
                    )
                )
            );

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, identity)
            .GetForChannelAsync(Broadcaster);

        result.IsSuccess.Should().BeTrue();
        IReadOnlyList<ChatEmote> emotes = result.Value;

        // Both pages of the user's cross-channel emotes reach the catalogue.
        emotes.Select(e => e.Code).Should().Contain(["subFromFriendA", "subFromFriendB"]);

        // Mapped to the Twitch-native ChatEmote shape: provider, set id, animated flag + the animated CDN url.
        ChatEmote b = emotes.Single(e => e.Code == "subFromFriendB");
        b.Provider.Should().Be(EmoteProvider.Twitch);
        b.Animated.Should().BeTrue();
        b.SetId.Should().Be("302");
        b.Urls["1"]
            .Should()
            .Be("https://static-cdn.jtvnw.net/emoticons/v2/subFromFriendB-id/animated/dark/1.0");

        // The second page was actually requested with the returned cursor.
        await assets
            .Received(1)
            .GetUserEmotesAsync(Broadcaster, "page2", Arg.Any<CancellationToken>());

        // The assembled set is cached against the resolved USER id (not the channel), so it can't leak across users.
        IReadOnlyList<ChatEmote>? userCached = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.TwitchUser(TwitchId)
        );
        userCached.Should().NotBeNull();
        userCached!
            .Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["subFromFriendA", "subFromFriendB"]);
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

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(TwitchId);

        Result<IReadOnlyList<ChatEmote>> result = await Build(cache, assets, identity)
            .GetForChannelAsync(Broadcaster);

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
