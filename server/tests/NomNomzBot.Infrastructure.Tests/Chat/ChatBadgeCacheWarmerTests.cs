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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Jobs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the badge warmer (chat-decoration spec §3.6/§7): a successful Helix fetch lands in cache under the global /
/// channel key, and a failed fetch leaves the previously cached set untouched (last-good, never wiped).
/// </summary>
public sealed class ChatBadgeCacheWarmerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000bb");

    private static TwitchChatBadgeSet Set(string setId) =>
        new(setId, [new TwitchChatBadgeVersion("0", "u1", "u2", "u4", "T", "D", "", "")]);

    private static ChatBadgeCacheWarmer Warmer(ICacheService cache, ITwitchChatAssetsApi assets)
    {
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.ChatAssets.Returns(assets);
        return new ChatBadgeCacheWarmer(helix, cache, NullLogger<ChatBadgeCacheWarmer>.Instance);
    }

    [Fact]
    public async Task WarmGlobalAsync_caches_the_global_badge_sets()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalChatBadgesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChatBadgeSet>>([Set("moderator")])
                )
            );

        bool warmed = await Warmer(cache, assets).WarmGlobalAsync();

        warmed.Should().BeTrue();
        IReadOnlyList<TwitchChatBadgeSet>? cached = await cache.GetAsync<
            IReadOnlyList<TwitchChatBadgeSet>
        >(ChatBadgeCacheKeys.Global);
        cached.Should().ContainSingle().Which.SetId.Should().Be("moderator");
    }

    [Fact]
    public async Task WarmGlobalAsync_keeps_last_good_on_failure()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Global,
            [Set("old")]
        );
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetGlobalChatBadgesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Failure<IReadOnlyList<TwitchChatBadgeSet>>("helix down", "TWITCH_ERROR")
                )
            );

        bool warmed = await Warmer(cache, assets).WarmGlobalAsync();

        warmed.Should().BeFalse();
        IReadOnlyList<TwitchChatBadgeSet>? cached = await cache.GetAsync<
            IReadOnlyList<TwitchChatBadgeSet>
        >(ChatBadgeCacheKeys.Global);
        cached.Should().ContainSingle().Which.SetId.Should().Be("old");
    }

    [Fact]
    public async Task WarmChannelAsync_caches_under_the_channel_key()
    {
        FakeCache cache = new();
        ITwitchChatAssetsApi assets = Substitute.For<ITwitchChatAssetsApi>();
        assets
            .GetChannelChatBadgesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChatBadgeSet>>([Set("subscriber")])
                )
            );

        await Warmer(cache, assets).WarmChannelAsync(Broadcaster);

        IReadOnlyList<TwitchChatBadgeSet>? cached = await cache.GetAsync<
            IReadOnlyList<TwitchChatBadgeSet>
        >(ChatBadgeCacheKeys.Channel(Broadcaster));
        cached.Should().ContainSingle().Which.SetId.Should().Be("subscriber");
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
