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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class SpotifyRateLimitCooldownsTests
{
    private static readonly Guid Channel = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_cooldown_survives_a_restart()
    {
        DictionaryCache cache = new();
        SpotifyRateLimitCooldowns before = new(new NullActionRequiredChangeNotifier(), cache);
        before.CoolUntil(Channel, Now, Now.AddHours(7));
        await Task.Yield();

        // A new instance is what a restarted process sees.
        SpotifyRateLimitCooldowns after = new(new NullActionRequiredChangeNotifier(), cache);
        after.TryGetCooldown(Channel, Now, out _).Should().BeFalse("nothing is loaded yet");

        await after.RestoreAsync(Channel, Now, CancellationToken.None);

        after
            .TryGetCooldown(Channel, Now.AddMinutes(1), out SpotifyCooldown restored)
            .Should()
            .BeTrue();
        restored.Since.Should().Be(Now);
        restored.Until.Should().Be(Now.AddHours(7));
    }

    [Fact]
    public async Task An_expired_saved_cooldown_is_not_restored()
    {
        DictionaryCache cache = new();
        SpotifyRateLimitCooldowns before = new(new NullActionRequiredChangeNotifier(), cache);
        before.CoolUntil(Channel, Now, Now.AddMinutes(5));
        await Task.Yield();

        SpotifyRateLimitCooldowns after = new(new NullActionRequiredChangeNotifier(), cache);
        await after.RestoreAsync(Channel, Now.AddMinutes(6), CancellationToken.None);

        after.TryGetCooldown(Channel, Now.AddMinutes(6), out _).Should().BeFalse();
    }

    [Fact]
    public void Calls_are_counted_per_channel_and_split_by_who_asked_and_age_out_after_an_hour()
    {
        SpotifyRateLimitCooldowns sut = new(new NullActionRequiredChangeNotifier());
        Guid other = Guid.NewGuid();
        sut.RecordCall(Channel, Now, isBackgroundPoll: true);
        sut.RecordCall(Channel, Now.AddSeconds(5), isBackgroundPoll: true);
        sut.RecordCall(Channel, Now.AddSeconds(6), isBackgroundPoll: false);
        sut.RecordCall(other, Now, isBackgroundPoll: false);

        SpotifyCallCounts counts = sut.CallsInLastHour(Channel, Now.AddMinutes(1));
        counts.Background.Should().Be(2);
        counts.Interactive.Should().Be(1);
        sut.CallsInLastHour(other, Now.AddMinutes(1)).Total.Should().Be(1);

        sut.CallsInLastHour(Channel, Now.AddMinutes(61)).Total.Should().Be(0);
    }

    [Fact]
    public void A_silent_player_answer_is_remembered_for_readers_only_until_it_goes_stale()
    {
        NowPlayingCache sut = new();
        DateTimeOffset seen = DateTimeOffset.UtcNow;
        sut.IsKnownEmpty(Channel, TimeSpan.FromSeconds(1)).Should().BeFalse();

        sut.SetEmpty(Channel, seen);
        sut.IsKnownEmpty(Channel, TimeSpan.FromSeconds(1)).Should().BeTrue();

        sut.SetEmpty(Channel, seen.AddSeconds(-5));
        sut.IsKnownEmpty(Channel, TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void A_real_track_clears_the_silent_answer()
    {
        NowPlayingCache sut = new();
        sut.SetEmpty(Channel, DateTimeOffset.UtcNow);

        sut.Set(
            Channel,
            new TrackInfo
            {
                TrackName = "Song",
                Artist = "A",
                Album = "B",
                TrackUri = "u",
                Provider = "spotify",
            },
            DateTimeOffset.UtcNow
        );

        sut.IsKnownEmpty(Channel, TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    private sealed class DictionaryCache : ICacheService
    {
        private readonly Dictionary<string, object> _items = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_items.TryGetValue(key, out object? v) ? (T?)v : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _items[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _items.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_items.ContainsKey(key));
    }
}
