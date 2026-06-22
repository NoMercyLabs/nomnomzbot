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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Chat.Jobs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the on-live warm (chat-decoration spec §3.6): when a channel goes online, the handler resolves its Twitch
/// id/login from the registry and warms that channel's emote sets into cache immediately; an unknown channel warms nothing.
/// </summary>
public sealed class StreamWentLiveEmoteWarmerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-000000000001");

    [Fact]
    public async Task Going_live_warms_the_channel_emote_sets_under_its_twitch_id()
    {
        FakeCache cache = new();
        ChatEmoteCacheWarmer warmer = WarmerReturning(
            cache,
            Result.Success<IReadOnlyList<ChatEmote>>([Emote("ChannelPog")])
        );

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(Broadcaster)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Broadcaster,
                    TwitchChannelId = "777",
                    ChannelName = "stoney_eagle",
                }
            );

        await new StreamWentLiveEmoteWarmer(channels, warmer).HandleAsync(LiveEvent());

        IReadOnlyList<ChatEmote>? cached = await cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, "777")
        );
        cached.Should().ContainSingle().Which.Code.Should().Be("ChannelPog");
    }

    [Fact]
    public async Task An_unknown_channel_warms_nothing()
    {
        FakeCache cache = new();
        ChatEmoteCacheWarmer warmer = WarmerReturning(
            cache,
            Result.Success<IReadOnlyList<ChatEmote>>([Emote("ChannelPog")])
        );

        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        await new StreamWentLiveEmoteWarmer(channels, warmer).HandleAsync(LiveEvent());

        (await cache.ExistsAsync(ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, "777")))
            .Should()
            .BeFalse();
    }

    private static ChatEmoteCacheWarmer WarmerReturning(
        ICacheService cache,
        Result<IReadOnlyList<ChatEmote>> channelResult
    )
    {
        IThirdPartyEmoteProvider provider = Substitute.For<IThirdPartyEmoteProvider>();
        provider.Provider.Returns(EmoteProvider.SevenTv);
        provider
            .GetChannelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channelResult));

        IThirdPartyEmoteProviderRegistry registry =
            Substitute.For<IThirdPartyEmoteProviderRegistry>();
        registry.All.Returns(new[] { provider });

        return new ChatEmoteCacheWarmer(registry, cache, NullLogger<ChatEmoteCacheWarmer>.Instance);
    }

    private static ChannelOnlineEvent LiveEvent() =>
        new()
        {
            BroadcasterId = Broadcaster,
            BroadcasterDisplayName = "Stoney_Eagle",
            StreamTitle = "live",
            GameName = "Just Chatting",
            StartedAt = DateTimeOffset.UnixEpoch,
        };

    private static ChatEmote Emote(string code) =>
        new(
            EmoteProvider.SevenTv,
            "id",
            code,
            new Dictionary<string, string> { ["1"] = "https://cdn/1x" },
            Animated: false,
            ZeroWidth: false
        );

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
