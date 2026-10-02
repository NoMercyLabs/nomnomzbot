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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

// Live 2026-10-01/02: Spotify blocked a channel's own app for hours, twice. Music stopped and nothing on the
// dashboard said why, when it would end, or that a new Spotify app fixes it at once.
public sealed class SpotifyAppBlockedSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000005b1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000005b2"
    );
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 14, 9, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BlockEnd = new(2026, 10, 2, 20, 47, 3, TimeSpan.Zero);

    private sealed class Fixture
    {
        public FakeTimeProvider Clock { get; } = new(T0);
        public RecordingChangeNotifier Inbox { get; } = new();
        public SpotifyRateLimitCooldowns Cooldowns { get; }
        public SpotifyAppBlockedSource Sut { get; }

        public Fixture()
        {
            Cooldowns = new(Inbox);
            Sut = new(Cooldowns, Clock);
        }

        public async Task<List<ActionRequiredItemDto>> ItemsAsync(
            IReadOnlySet<string>? dismissed = null
        )
        {
            Result<List<ActionRequiredItemDto>> result = await Sut.GetItemsAsync(
                ChannelId,
                dismissed ?? new HashSet<string>()
            );
            result.IsSuccess.Should().BeTrue();
            return result.Value;
        }
    }

    [Fact]
    public async Task A_long_block_raises_one_warning_that_names_when_it_ends()
    {
        Fixture f = new();
        f.Cooldowns.CoolUntil(ChannelId, T0, BlockEnd);

        ActionRequiredItemDto item = (await f.ItemsAsync()).Should().ContainSingle().Subject;
        item.Id.Should().Be($"spotify-blocked:{ChannelId}:{BlockEnd.UtcTicks}");
        item.Kind.Should().Be("spotify_app_blocked");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_spotify_blocked_title");
        item.MessageKey.Should().Be("attention_spotify_blocked_message");
        item.Parameters.Should()
            .Equal(
                new Dictionary<string, string> { ["until"] = "2026-10-02T20:47:03.0000000+00:00" }
            );
        item.DetectedAt.Should().Be(T0.UtcDateTime);
        item.DeepLinkRoute.Should().Be("integrations");
        item.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_short_rate_limit_is_no_block_and_raises_nothing()
    {
        Fixture f = new();
        f.Cooldowns.CoolUntil(ChannelId, T0, T0 + TimeSpan.FromSeconds(30));

        (await f.ItemsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_warning_clears_when_the_block_ends_and_the_dashboards_are_told()
    {
        Fixture f = new();
        f.Cooldowns.CoolUntil(ChannelId, T0, BlockEnd);
        f.Inbox.Signalled.Should().Equal(ChannelId);

        f.Clock.SetUtcNow(BlockEnd.AddSeconds(1));

        (await f.ItemsAsync()).Should().BeEmpty();
        f.Inbox.Signalled.Should().Equal(ChannelId, ChannelId);
    }

    [Fact]
    public async Task A_dismissed_block_stays_hidden_but_the_next_block_shows_again()
    {
        Fixture f = new();
        f.Cooldowns.CoolUntil(ChannelId, T0, BlockEnd);
        string dismissed = (await f.ItemsAsync()).Single().Id;

        (await f.ItemsAsync(new HashSet<string> { dismissed })).Should().BeEmpty();

        DateTimeOffset nextDay = T0.AddDays(1);
        f.Clock.SetUtcNow(nextDay);
        f.Cooldowns.CoolUntil(ChannelId, nextDay, BlockEnd.AddDays(1));

        (await f.ItemsAsync(new HashSet<string> { dismissed })).Should().ContainSingle();
    }

    [Fact]
    public async Task Another_channel_s_block_is_not_shown()
    {
        Fixture f = new();
        f.Cooldowns.CoolUntil(OtherChannelId, T0, BlockEnd);

        (await f.ItemsAsync()).Should().BeEmpty();
    }
}
