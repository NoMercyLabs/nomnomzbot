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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Jobs;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the refresh worker's warming logic (chat-decoration spec §3.6/§7): a successful provider fetch lands in the
/// cache under the shared global key, and a provider <b>failure</b> leaves the previously cached set untouched — the
/// stale-but-good entry is never wiped, so the reader keeps matching emotes through a provider outage.
/// </summary>
public sealed class ChatEmoteCacheWarmerTests
{
    private static ChatEmote Emote(EmoteProvider provider, string code, string id) =>
        new(
            provider,
            id,
            code,
            new Dictionary<string, string> { ["1"] = $"https://cdn/{id}/1x" },
            Animated: false,
            ZeroWidth: false
        );

    private static ChatEmoteCacheWarmer Warmer(
        ICacheService cache,
        params FakeProvider[] providers
    ) => new(new FakeRegistry(providers), cache, NullLogger<ChatEmoteCacheWarmer>.Instance);

    [Fact]
    public async Task WarmGlobalAsync_caches_each_provider_global_set()
    {
        FakeCache cache = new();
        ChatEmoteCacheWarmer warmer = Warmer(
            cache,
            new FakeProvider(
                EmoteProvider.SevenTv,
                Result.Success<IReadOnlyList<ChatEmote>>([
                    Emote(EmoteProvider.SevenTv, "PepeLaugh", "1"),
                ])
            ),
            new FakeProvider(
                EmoteProvider.Bttv,
                Result.Success<IReadOnlyList<ChatEmote>>([
                    Emote(EmoteProvider.Bttv, "OMEGALUL", "2"),
                ])
            )
        );

        int warmed = await warmer.WarmGlobalAsync();

        warmed.Should().Be(2);
        IReadOnlyList<ChatEmote>? sevenTv = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv)
        );
        sevenTv.Should().ContainSingle().Which.Code.Should().Be("PepeLaugh");
        IReadOnlyList<ChatEmote>? bttv = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.Bttv)
        );
        bttv.Should().ContainSingle().Which.Code.Should().Be("OMEGALUL");
    }

    [Fact]
    public async Task WarmGlobalAsync_keeps_last_good_cache_when_a_provider_fails()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [Emote(EmoteProvider.SevenTv, "OldEmote", "old")]
        );

        ChatEmoteCacheWarmer warmer = Warmer(
            cache,
            new FakeProvider(
                EmoteProvider.SevenTv,
                Result.Failure<IReadOnlyList<ChatEmote>>("provider down", "EMOTE_PROVIDER_ERROR")
            )
        );

        int warmed = await warmer.WarmGlobalAsync();

        warmed.Should().Be(0);
        IReadOnlyList<ChatEmote>? stillCached = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv)
        );
        stillCached.Should().ContainSingle().Which.Code.Should().Be("OldEmote");
    }

    private sealed class FakeProvider(
        EmoteProvider provider,
        Result<IReadOnlyList<ChatEmote>> global
    ) : IThirdPartyEmoteProvider
    {
        public EmoteProvider Provider { get; } = provider;

        public Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(
            CancellationToken ct = default
        ) => Task.FromResult(global);

        public Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
            string twitchBroadcasterId,
            string broadcasterLogin,
            CancellationToken ct = default
        ) => Task.FromResult(global);
    }

    private sealed class FakeRegistry(IReadOnlyCollection<IThirdPartyEmoteProvider> providers)
        : IThirdPartyEmoteProviderRegistry
    {
        public IReadOnlyCollection<IThirdPartyEmoteProvider> All { get; } = providers;

        public IThirdPartyEmoteProvider? Get(EmoteProvider provider) =>
            All.FirstOrDefault(candidate => candidate.Provider == provider);
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
