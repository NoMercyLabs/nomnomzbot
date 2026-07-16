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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Jobs;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the periodic decoration warm covers every channel the bot serves, live OR offline (chat-decoration §3.6).
/// The dashboard decorates an offline channel's chat and history too, so warming only the live channels left offline
/// channels' third-party (7TV/BTTV/FFZ) sets cold and their codes rendering as plain text — the reported bug. This
/// regresses (a revert to <c>GetLiveChannels()</c>) for the right reason: the offline channel's emote key stays absent.
/// </summary>
public sealed class ChatDecorationRefreshServiceTests
{
    [Fact]
    public async Task WarmActiveChannels_warms_offline_channels_too_not_only_live()
    {
        FakeCache cache = new();
        ChatEmoteCacheWarmer warmer = new(
            new FakeProviderRegistry(new FakeSevenTvProvider()),
            cache,
            NullLogger<ChatEmoteCacheWarmer>.Instance
        );

        ChannelContext live = Ctx("live_channel", twitchId: "111", isLive: true);
        ChannelContext offline = Ctx("offline_channel", twitchId: "222", isLive: false);
        FakeChannelRegistry registry = new(all: [live, offline], liveOnly: [live]);

        // A real DI scope factory whose only registration is a "not configured" bot-readiness gate, so the
        // Helix badge/cheermote branch short-circuits and the test needs no Helix doubles.
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IPlatformBotReadinessGate>(new NotConfiguredGate())
            .BuildServiceProvider();

        ChatDecorationRefreshService service = new(
            warmer,
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatDecorationRefreshService>.Instance
        );

        await service.WarmActiveChannelsAsync(CancellationToken.None);

        IReadOnlyList<ChatEmote>? liveSet = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, "111")
        );
        IReadOnlyList<ChatEmote>? offlineSet = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, "222")
        );

        liveSet.Should().ContainSingle().Which.Code.Should().Be("channelPog");
        offlineSet
            .Should()
            .ContainSingle(
                "an offline but active channel must still have its channel emotes warmed"
            )
            .Which.Code.Should()
            .Be("channelPog");
    }

    private static ChannelContext Ctx(string name, string twitchId, bool isLive) =>
        new()
        {
            BroadcasterId = Guid.NewGuid(),
            TwitchChannelId = twitchId,
            ChannelName = name,
            IsLive = isLive,
        };

    private sealed class FakeSevenTvProvider : IThirdPartyEmoteProvider
    {
        public EmoteProvider Provider => EmoteProvider.SevenTv;

        public Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<ChatEmote>>([]));

        public Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
            string twitchBroadcasterId,
            string broadcasterLogin,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success<IReadOnlyList<ChatEmote>>([
                    new ChatEmote(
                        EmoteProvider.SevenTv,
                        $"e-{twitchBroadcasterId}",
                        "channelPog",
                        new Dictionary<string, string> { ["1"] = "https://cdn/7tv/1x" },
                        Animated: false,
                        ZeroWidth: false
                    ),
                ])
            );
    }

    private sealed class FakeProviderRegistry(IThirdPartyEmoteProvider provider)
        : IThirdPartyEmoteProviderRegistry
    {
        public IReadOnlyCollection<IThirdPartyEmoteProvider> All { get; } = [provider];

        public IThirdPartyEmoteProvider? Get(EmoteProvider p) =>
            All.FirstOrDefault(candidate => candidate.Provider == p);
    }

    private sealed class FakeChannelRegistry(
        IReadOnlyCollection<ChannelContext> all,
        IReadOnlyCollection<ChannelContext> liveOnly
    ) : IChannelRegistry
    {
        public IReadOnlyCollection<ChannelContext> GetAll() => all;

        public IReadOnlyCollection<ChannelContext> GetLiveChannels() => liveOnly;

        public int Count => all.Count;

        public ChannelContext? Get(Guid broadcasterId) =>
            all.FirstOrDefault(c => c.BroadcasterId == broadcasterId);

        public Task<ChannelContext> GetOrCreateAsync(
            Guid broadcasterId,
            string twitchChannelId,
            string channelName,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task InvalidateCommandsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateBuiltinsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateSettingsAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvalidateChatTriggersAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task RemoveAsync(Guid broadcasterId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NotConfiguredGate : IPlatformBotReadinessGate
    {
        public Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
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
