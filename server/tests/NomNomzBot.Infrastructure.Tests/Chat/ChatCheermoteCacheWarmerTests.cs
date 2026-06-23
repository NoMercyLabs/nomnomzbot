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
/// Proves the cheermote warmer (chat-decoration spec §3.6/§7): a successful Helix fetch lands in cache under the
/// channel key, and a failed fetch leaves the previously cached set untouched (last-good).
/// </summary>
public sealed class ChatCheermoteCacheWarmerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000dd");

    private static TwitchCheermote Cheermote(string prefix) =>
        new(prefix, [], "prefix", 1, DateTimeOffset.UnixEpoch, IsCharitable: false);

    private static ChatCheermoteCacheWarmer Warmer(
        ICacheService cache,
        Result<IReadOnlyList<TwitchCheermote>> result
    )
    {
        ITwitchBitsApi bits = Substitute.For<ITwitchBitsApi>();
        bits.GetCheermotesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Bits.Returns(bits);
        return new ChatCheermoteCacheWarmer(
            helix,
            cache,
            NullLogger<ChatCheermoteCacheWarmer>.Instance
        );
    }

    [Fact]
    public async Task WarmChannelAsync_caches_the_channel_cheermotes()
    {
        FakeCache cache = new();
        ChatCheermoteCacheWarmer warmer = Warmer(
            cache,
            Result.Success<IReadOnlyList<TwitchCheermote>>([Cheermote("Cheer")])
        );

        bool warmed = await warmer.WarmChannelAsync(Broadcaster);

        warmed.Should().BeTrue();
        IReadOnlyList<TwitchCheermote>? cached = await cache.GetAsync<
            IReadOnlyList<TwitchCheermote>
        >(ChatCheermoteCacheKeys.Channel(Broadcaster));
        cached.Should().ContainSingle().Which.Prefix.Should().Be("Cheer");
    }

    [Fact]
    public async Task WarmChannelAsync_keeps_last_good_on_failure()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchCheermote>>(
            ChatCheermoteCacheKeys.Channel(Broadcaster),
            [Cheermote("OldCheer")]
        );
        ChatCheermoteCacheWarmer warmer = Warmer(
            cache,
            Result.Failure<IReadOnlyList<TwitchCheermote>>("helix down", "TWITCH_ERROR")
        );

        bool warmed = await warmer.WarmChannelAsync(Broadcaster);

        warmed.Should().BeFalse();
        IReadOnlyList<TwitchCheermote>? cached = await cache.GetAsync<
            IReadOnlyList<TwitchCheermote>
        >(ChatCheermoteCacheKeys.Channel(Broadcaster));
        cached.Should().ContainSingle().Which.Prefix.Should().Be("OldCheer");
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
