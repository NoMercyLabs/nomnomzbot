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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the cheermote step (chat-decoration spec §3.4): a cheermote resolves to the image of the tier the cheer
/// qualified for (the highest MinBits not exceeding the bits cheered, falling back to the lowest tier), case-insensitive
/// on prefix, returning the dark-theme animated urls + the tier colour; an unknown prefix or un-warmed channel is null.
/// </summary>
public sealed class ChatCheermoteResolverTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000cc");

    private static TwitchCheermoteTier Tier(int minBits, string color, string baseUrl) =>
        new(
            minBits,
            minBits.ToString(),
            color,
            new TwitchCheermoteImages(
                new TwitchCheermoteImageFormats(
                    new TwitchCheermoteImageScales(
                        new Dictionary<string, string> { ["1"] = $"{baseUrl}/light-anim" }
                    ),
                    new TwitchCheermoteImageScales(
                        new Dictionary<string, string> { ["1"] = $"{baseUrl}/light-static" }
                    )
                ),
                new TwitchCheermoteImageFormats(
                    new TwitchCheermoteImageScales(
                        new Dictionary<string, string> { ["1"] = $"{baseUrl}/dark-anim" }
                    ),
                    new TwitchCheermoteImageScales(
                        new Dictionary<string, string> { ["1"] = $"{baseUrl}/dark-static" }
                    )
                )
            ),
            CanCheer: true,
            ShowInBitsCard: true
        );

    private static TwitchCheermote Cheermote(string prefix, params TwitchCheermoteTier[] tiers) =>
        new(prefix, tiers, "prefix", 1, DateTimeOffset.UnixEpoch, IsCharitable: false);

    private static async Task<ChatCheermoteResolver> ResolverWith(
        params TwitchCheermote[] cheermotes
    )
    {
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<TwitchCheermote>>(
            ChatCheermoteCacheKeys.Channel(Broadcaster),
            cheermotes
        );
        return new ChatCheermoteResolver(cache);
    }

    [Fact]
    public async Task Resolves_the_tier_the_cheer_qualifies_for_with_dark_animated_urls()
    {
        ChatCheermoteResolver resolver = await ResolverWith(
            Cheermote(
                "Cheer",
                Tier(1, "#aa0000", "https://cdn/1"),
                Tier(100, "#00aa00", "https://cdn/100"),
                Tier(1000, "#0000aa", "https://cdn/1000")
            )
        );

        CheermoteImage? image = await resolver.ResolveAsync(Broadcaster, "Cheer", 150, 2);

        image.Should().NotBeNull();
        image!.ColorHex.Should().Be("#00aa00"); // tier MinBits 100 is the highest not exceeding 150
        image.Animated.Should().BeTrue();
        image.Urls["1"].Should().Be("https://cdn/100/dark-anim");
    }

    [Fact]
    public async Task Falls_back_to_the_lowest_tier_when_bits_are_below_every_threshold()
    {
        ChatCheermoteResolver resolver = await ResolverWith(
            Cheermote("Cheer", Tier(100, "#00aa00", "https://cdn/100"))
        );

        CheermoteImage? image = await resolver.ResolveAsync(Broadcaster, "cheer", 5, 1);

        image.Should().NotBeNull();
        image!.ColorHex.Should().Be("#00aa00"); // only tier — used even though 5 < 100, and prefix match is case-insensitive
    }

    [Fact]
    public async Task An_unknown_prefix_resolves_to_null()
    {
        ChatCheermoteResolver resolver = await ResolverWith(
            Cheermote("Cheer", Tier(1, "#aa0000", "https://cdn/1"))
        );

        (await resolver.ResolveAsync(Broadcaster, "PogChamp", 100, 1)).Should().BeNull();
    }

    [Fact]
    public async Task An_unwarmed_channel_resolves_to_null()
    {
        CheermoteImage? image = await new ChatCheermoteResolver(new FakeCache()).ResolveAsync(
            Broadcaster,
            "Cheer",
            100,
            1
        );

        image.Should().BeNull();
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
