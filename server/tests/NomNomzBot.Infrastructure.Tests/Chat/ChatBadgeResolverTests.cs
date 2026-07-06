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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the badge step (chat-decoration spec §3.3): a message badge resolves to its scale-keyed image urls from the
/// cached Helix sets, a channel's own version overrides the global on a collision, and an un-warmed badge still comes
/// through (empty urls) rather than being dropped.
/// </summary>
public sealed class ChatBadgeResolverTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000aa");

    private static TwitchChatBadgeSet Set(string setId, string versionId, string baseUrl) =>
        new(
            setId,
            [
                new TwitchChatBadgeVersion(
                    versionId,
                    $"{baseUrl}/1",
                    $"{baseUrl}/2",
                    $"{baseUrl}/4",
                    "Title",
                    "Desc",
                    "",
                    ""
                ),
            ]
        );

    [Fact]
    public async Task Resolves_badge_urls_from_the_global_set()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Global,
            [Set("moderator", "1", "https://badge/mod")]
        );

        IReadOnlyList<ResolvedChatBadge> resolved = await new ChatBadgeResolver(cache).ResolveAsync(
            Broadcaster,
            [new ChatBadge("moderator", "1")]
        );

        ResolvedChatBadge badge = resolved.Should().ContainSingle().Subject;
        badge.SetId.Should().Be("moderator");
        badge.Urls["1"].Should().Be("https://badge/mod/1");
        badge.Urls["2"].Should().Be("https://badge/mod/2");
        badge.Urls["4"].Should().Be("https://badge/mod/4");
    }

    [Fact]
    public async Task Channel_set_overrides_global_for_the_same_set_and_version()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Global,
            [Set("subscriber", "0", "https://badge/global-sub")]
        );
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Channel(Broadcaster),
            [Set("subscriber", "0", "https://badge/channel-sub")]
        );

        IReadOnlyList<ResolvedChatBadge> resolved = await new ChatBadgeResolver(cache).ResolveAsync(
            Broadcaster,
            [new ChatBadge("subscriber", "0")]
        );

        resolved
            .Should()
            .ContainSingle()
            .Which.Urls["1"]
            .Should()
            .Be("https://badge/channel-sub/1");
    }

    [Fact]
    public async Task An_unwarmed_badge_resolves_to_empty_urls_but_is_still_emitted()
    {
        IReadOnlyList<ResolvedChatBadge> resolved = await new ChatBadgeResolver(
            new FakeCache()
        ).ResolveAsync(Broadcaster, [new ChatBadge("vip", "1", "info")]);

        ResolvedChatBadge badge = resolved.Should().ContainSingle().Subject;
        badge.SetId.Should().Be("vip");
        badge.Info.Should().Be("info");
        badge.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_image_urls_are_omitted_never_emitted_as_null_values()
    {
        // A cached version can carry null image urls (a partially-populated Helix payload deserialized into the
        // non-null string fields). The resolver must NOT put null VALUES into the url map — {"1":null,...} would
        // serialize null into a Dictionary<string,string> and break the client's JSON parse of the WHOLE feed.
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Global,
            [
                new TwitchChatBadgeSet(
                    "moderator",
                    [new TwitchChatBadgeVersion("1", null!, null!, null!, "Title", "Desc", "", "")]
                ),
            ]
        );

        IReadOnlyList<ResolvedChatBadge> resolved = await new ChatBadgeResolver(cache).ResolveAsync(
            Broadcaster,
            [new ChatBadge("moderator", "1")]
        );

        ResolvedChatBadge badge = resolved.Should().ContainSingle().Subject;
        badge.Urls.Should().BeEmpty();
        badge.Urls.Values.Should().NotContainNulls();
    }

    [Fact]
    public async Task Partial_null_image_urls_keep_only_the_resolved_sizes()
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchChatBadgeSet>>(
            ChatBadgeCacheKeys.Global,
            [
                new TwitchChatBadgeSet(
                    "subscriber",
                    [
                        new TwitchChatBadgeVersion(
                            "0",
                            "https://badge/sub/1",
                            null!,
                            "",
                            "T",
                            "D",
                            "",
                            ""
                        ),
                    ]
                ),
            ]
        );

        IReadOnlyList<ResolvedChatBadge> resolved = await new ChatBadgeResolver(cache).ResolveAsync(
            Broadcaster,
            [new ChatBadge("subscriber", "0")]
        );

        IReadOnlyDictionary<string, string> urls = resolved.Should().ContainSingle().Subject.Urls;
        urls.Should().ContainKey("1").WhoseValue.Should().Be("https://badge/sub/1");
        urls.Should().NotContainKey("2"); // null → omitted
        urls.Should().NotContainKey("4"); // empty → omitted
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
