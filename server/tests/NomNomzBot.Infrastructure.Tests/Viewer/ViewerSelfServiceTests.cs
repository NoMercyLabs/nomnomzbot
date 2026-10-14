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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Services.Viewer;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Viewer;

/// <summary>
/// Proves the viewer's self-only reads: the watched-channel list (the caller's channels, newest first, never an
/// owned one) and the per-channel own tiles (the caller's counts, never another viewer's; points and song-request
/// flags; the zero shape for no activity; NOT_FOUND only for an unknown channel).
/// </summary>
public sealed class ViewerSelfServiceTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-0000000005a1");
    private static readonly Guid OtherViewer = Guid.Parse("0192a000-0000-7000-8000-0000000005b2");
    private static readonly Guid ChannelOne = Guid.Parse("0192a000-0000-7000-8000-000000005c01");
    private static readonly Guid ChannelTwo = Guid.Parse("0192a000-0000-7000-8000-000000005c02");
    private static readonly Guid OwnChannel = Guid.Parse("0192a000-0000-7000-8000-000000005c03");
    private static readonly Guid Streamer = Guid.Parse("0192a000-0000-7000-8000-000000005d01");

    private sealed record Harness(
        ViewerSelfService Sut,
        AuthDbContext Db,
        IMusicConfigService Music,
        IFollowStateService Follows
    );

    private static Harness Build(bool songRequestsOn = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IMusicConfigService music = Substitute.For<IMusicConfigService>();
        music
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(MusicConfig(songRequestsOn)));
        IFollowStateService follows = Substitute.For<IFollowStateService>();
        follows
            .ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(FollowLookup.Unknown);
        return new(new(db, music, follows), db, music, follows);
    }

    private static MusicConfigDto MusicConfig(bool enabled) =>
        new(enabled, "auto", 50, 5, true, true, "everyone");

    private static async Task SeedUserAsync(AuthDbContext db, Guid id, string name, string twitchId)
    {
        db.Users.Add(
            new()
            {
                Id = id,
                TwitchUserId = twitchId,
                Username = name,
                UsernameNormalized = name,
                DisplayName = name.ToUpperInvariant(),
                ProfileImageUrl = $"https://img/{name}.png",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedChannelAsync(
        AuthDbContext db,
        Guid channelId,
        Guid ownerId,
        string name,
        bool isLive = false
    )
    {
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = channelId.ToString("N")[..12],
                Name = name,
                NameNormalized = name,
                IsLive = isLive,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedProfileAsync(
        AuthDbContext db,
        Guid channelId,
        Guid viewerId,
        DateTime lastSeen,
        long messages = 0,
        long commands = 0,
        long songs = 0,
        string? subTier = null
    )
    {
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = channelId,
                ViewerUserId = viewerId,
                ViewerTwitchUserId = viewerId.ToString("N")[..10],
                FirstSeenAt = lastSeen.AddDays(-30),
                LastSeenAt = lastSeen,
                TotalMessages = messages,
                TotalCommandsUsed = commands,
                TotalSongRequests = songs,
                IsSubscriber = subTier is not null,
                SubTier = subTier,
            }
        );
        await db.SaveChangesAsync();
    }

    private static DateTime Day(int day) => new(2026, 10, day, 12, 0, 0, DateTimeKind.Utc);

    private static async Task SeedWorldAsync(AuthDbContext db)
    {
        await SeedUserAsync(db, Caller, "caller", "9001");
        await SeedUserAsync(db, OtherViewer, "other", "9002");
        await SeedUserAsync(db, Streamer, "streamer", "9003");
        await SeedChannelAsync(db, ChannelOne, Streamer, "alpha", isLive: true);
        await SeedChannelAsync(db, ChannelTwo, Streamer, "beta");
        await SeedChannelAsync(db, OwnChannel, Caller, "caller");
    }

    private static async Task SeedCurrencyAsync(AuthDbContext db, bool enabled)
    {
        db.CurrencyConfigs.Add(
            new()
            {
                BroadcasterId = ChannelOne,
                CurrencyName = "bits",
                IsEnabled = enabled,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedAccountAsync(
        AuthDbContext db,
        Guid viewerId,
        string twitchId,
        long balance
    )
    {
        db.CurrencyAccounts.Add(
            new()
            {
                BroadcasterId = ChannelOne,
                ViewerUserId = viewerId,
                ViewerTwitchUserId = twitchId,
                Balance = balance,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task WatchedChannels_lists_both_channels_newest_first_and_never_the_callers_own()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedProfileAsync(h.Db, ChannelOne, Caller, Day(1));
        await SeedProfileAsync(h.Db, ChannelTwo, Caller, Day(9));
        await SeedProfileAsync(h.Db, OwnChannel, Caller, Day(12));
        await SeedProfileAsync(h.Db, ChannelOne, OtherViewer, Day(13));

        Result<IReadOnlyList<WatchedChannelDto>> result = await h.Sut.ListWatchedChannelsAsync(
            Caller
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Select(c => c.ChannelId)
            .Should()
            .Equal(ChannelTwo.ToString(), ChannelOne.ToString());
        WatchedChannelDto older = result.Value[1];
        older.Name.Should().Be("alpha");
        older.DisplayName.Should().Be("STREAMER");
        older.ProfileImageUrl.Should().Be("https://img/streamer.png");
        older.IsLive.Should().BeTrue();
        older.LastSeenAt.Should().Be(Day(1));
        result.Value[0].IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task WatchedChannels_is_empty_for_a_caller_with_no_activity()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedProfileAsync(h.Db, ChannelOne, OtherViewer, Day(13));

        Result<IReadOnlyList<WatchedChannelDto>> result = await h.Sut.ListWatchedChannelsAsync(
            Caller
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Summary_returns_the_callers_counts_and_not_another_viewers()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedProfileAsync(
            h.Db,
            ChannelOne,
            Caller,
            Day(1),
            messages: 3137,
            commands: 463,
            songs: 7,
            subTier: "2000"
        );
        await SeedProfileAsync(
            h.Db,
            ChannelOne,
            OtherViewer,
            Day(13),
            messages: 99999,
            commands: 88888,
            songs: 77777
        );

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelOne, Caller);

        result.IsSuccess.Should().BeTrue();
        ViewerSelfSummaryDto tiles = result.Value;
        tiles.TotalMessages.Should().Be(3137);
        tiles.TotalCommandsUsed.Should().Be(463);
        tiles.TotalSongRequests.Should().Be(7);
        tiles.FirstSeenAt.Should().Be(Day(1).AddDays(-30));
        tiles.LastSeenAt.Should().Be(Day(1));
        tiles.SubTier.Should().Be("2000");
        tiles.IsLive.Should().BeTrue();
    }

    [Fact]
    public async Task Summary_with_points_off_reports_disabled_and_a_null_balance()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedCurrencyAsync(h.Db, enabled: false);
        await SeedAccountAsync(h.Db, Caller, "9001", 250);

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelOne, Caller);

        result.Value.PointsEnabled.Should().BeFalse();
        result.Value.Balance.Should().BeNull();
    }

    [Fact]
    public async Task Summary_with_points_on_returns_only_the_callers_balance()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedCurrencyAsync(h.Db, enabled: true);
        await SeedAccountAsync(h.Db, Caller, "9001", 250);
        await SeedAccountAsync(h.Db, OtherViewer, "9002", 9999);

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelOne, Caller);

        result.Value.PointsEnabled.Should().BeTrue();
        result.Value.Balance.Should().Be(250);
    }

    [Fact]
    public async Task Summary_with_points_on_but_no_account_has_a_null_balance()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        await SeedCurrencyAsync(h.Db, enabled: true);

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelOne, Caller);

        result.Value.PointsEnabled.Should().BeTrue();
        result.Value.Balance.Should().BeNull();
    }

    [Fact]
    public async Task Summary_reports_whether_the_channel_takes_song_requests()
    {
        Harness on = Build(songRequestsOn: true);
        await SeedWorldAsync(on.Db);
        Harness off = Build(songRequestsOn: false);
        await SeedWorldAsync(off.Db);

        Result<ViewerSelfSummaryDto> onResult = await on.Sut.GetSummaryAsync(ChannelOne, Caller);
        Result<ViewerSelfSummaryDto> offResult = await off.Sut.GetSummaryAsync(ChannelOne, Caller);

        onResult.Value.SongRequestsEnabled.Should().BeTrue();
        offResult.Value.SongRequestsEnabled.Should().BeFalse();
        await off
            .Music.Received(1)
            .GetConfigAsync(ChannelOne.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Summary_carries_the_follow_date_when_the_platform_confirms_it()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);
        DateTimeOffset followed = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);
        h.Follows.ResolveAsync(ChannelOne, "twitch", "9001", Arg.Any<CancellationToken>())
            .Returns(new FollowLookup(FollowState.Following, followed));

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelOne, Caller);

        result.Value.FollowedAt.Should().Be(followed.UtcDateTime);
    }

    [Fact]
    public async Task Summary_without_activity_is_the_zero_shape_not_a_failure()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(ChannelTwo, Caller);

        result.IsSuccess.Should().BeTrue();
        ViewerSelfSummaryDto tiles = result.Value;
        tiles.FirstSeenAt.Should().BeNull();
        tiles.LastSeenAt.Should().BeNull();
        tiles.TotalMessages.Should().Be(0);
        tiles.TotalCommandsUsed.Should().Be(0);
        tiles.TotalSongRequests.Should().Be(0);
        tiles.SubTier.Should().BeNull();
        tiles.FollowedAt.Should().BeNull();
        tiles.PointsEnabled.Should().BeFalse();
        tiles.Balance.Should().BeNull();
        tiles.IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task Summary_for_an_unknown_channel_is_not_found()
    {
        Harness h = Build();
        await SeedWorldAsync(h.Db);

        Result<ViewerSelfSummaryDto> result = await h.Sut.GetSummaryAsync(
            Guid.Parse("0192a000-0000-7000-8000-000000005eee"),
            Caller
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }
}
