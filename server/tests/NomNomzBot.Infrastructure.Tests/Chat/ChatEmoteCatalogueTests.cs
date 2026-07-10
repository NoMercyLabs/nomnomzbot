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
        operatorCached!
            .Select(e => e.Code)
            .Should()
            .BeEquivalentTo(["subFromFriendA", "subFromFriendB"]);

        // Nothing is cached under the CHANNEL's id — proving the key is the operator's, not the tenant's.
        (await cache.GetAsync<IReadOnlyList<ChatEmote>>(ChatEmoteCacheKeys.TwitchUser(TwitchId)))
            .Should()
            .BeNull();
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
