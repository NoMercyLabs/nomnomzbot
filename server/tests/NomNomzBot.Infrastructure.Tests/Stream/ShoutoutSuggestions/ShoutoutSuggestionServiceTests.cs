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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Stream.ShoutoutSuggestions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.ShoutoutSuggestions;

/// <summary>
/// The shoutout suggestions list: chatters of THIS stream, most streams in the last 14 days first, with the
/// broadcaster, the bot, chatters without an archive, chatters already shouted out and dismissed chatters left
/// out, a cap of 10, and Twitch lookups kept for one hour.
/// </summary>
public sealed class ShoutoutSuggestionServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000a5001");
    private const string BroadcasterId = "1";
    private const string BotId = "2";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-10-13T20:00:00Z"));
    private readonly ITwitchVideosApi _videos = Substitute.For<ITwitchVideosApi>();
    private readonly ITwitchChannelsApi _channels = Substitute.For<ITwitchChannelsApi>();
    private readonly IChannelRegistry _registry = Substitute.For<IChannelRegistry>();
    private readonly ShoutoutSuggestionState _state = new();
    private readonly ChannelContext _context;
    private readonly ShoutoutSuggestionService _service;
    private readonly Dictionary<string, List<DateTimeOffset>> _archives = [];

    public ShoutoutSuggestionServiceTests()
    {
        _connection.Open();
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        _context = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = BroadcasterId,
            ChannelName = "streamer",
            IsLive = true,
            WentLiveAt = _clock.GetUtcNow().AddHours(-1),
        };
        _registry.Get(Channel).Returns(_context);

        _videos
            .GetVideosByTwitchUserIdAsync(
                Arg.Any<string>(),
                "archive",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string user = call.ArgAt<string>(0);
                List<TwitchVideo> items = _archives
                    .GetValueOrDefault(user, [])
                    .Select(at => Video(user, at))
                    .ToList();
                return Result.Success(new TwitchPage<TwitchVideo>(items, null, items.Count));
            });
        _channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                IReadOnlyList<TwitchChannelInformation> infos = call.ArgAt<IReadOnlyList<string>>(0)
                    .Select(id => Info(id))
                    .ToList();
                return Result.Success(infos);
            });

        _service = new(_registry, _db, _videos, _channels, _state, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static TwitchVideo Video(string user, DateTimeOffset at) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            StreamId: "s",
            UserId: user,
            UserLogin: "login" + user,
            UserName: "Name" + user,
            Title: "t",
            Description: "",
            CreatedAt: at,
            PublishedAt: at,
            Url: "u",
            ThumbnailUrl: "th",
            Viewable: "public",
            ViewCount: 1,
            Language: "en",
            Type: "archive",
            Duration: "1h",
            MutedSegments: null
        );

    private static TwitchChannelInformation Info(string id) =>
        new(
            BroadcasterId: id,
            BroadcasterLogin: "login" + id,
            BroadcasterName: "Name" + id,
            BroadcasterLanguage: "en",
            GameId: "g" + id,
            GameName: "Game " + id,
            Title: "t",
            Delay: 0,
            Tags: [],
            ContentClassificationLabels: [],
            IsBrandedContent: false
        );

    private void Chatter(string id, int archivesInWindow = 1)
    {
        _context.SessionChatters[id] = "Chat" + id;
        DateTimeOffset now = _clock.GetUtcNow();
        _archives[id] = Enumerable
            .Range(0, archivesInWindow)
            .Select(i => now.AddDays(-1 - i))
            .ToList();
    }

    private async Task<List<ShoutoutSuggestionDto>> SuggestAsync()
    {
        Result<IReadOnlyList<ShoutoutSuggestionDto>> result = await _service.GetAsync(Channel);
        result.IsSuccess.Should().BeTrue();
        return result.Value.ToList();
    }

    [Fact]
    public async Task Orders_by_streams_in_14_days_then_name_and_fills_every_field_from_twitch_or_our_records()
    {
        Chatter("11", 3);
        Chatter("12", 5);
        Chatter("10", 3);
        _db.ChatMessages.Add(
            new()
            {
                Id = "m1",
                BroadcasterId = Channel,
                Provider = "twitch",
                UserId = "12",
                Username = "login12",
                DisplayName = "Name12",
                UserType = "viewer",
                Message = "hi",
                ColorHex = "#FF8800",
                CreatedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
            }
        );
        DateTime shoutedBefore = _clock.GetUtcNow().UtcDateTime.AddDays(-3);
        _db.ChannelEvents.Add(ShoutoutEvent("12", shoutedBefore));
        await _db.SaveChangesAsync();

        List<ShoutoutSuggestionDto> list = await SuggestAsync();

        list.Select(s => s.TwitchUserId).Should().Equal("12", "10", "11");
        list.Select(s => s.StreamsLast14Days).Should().Equal(5, 3, 3);
        ShoutoutSuggestionDto top = list[0];
        top.Login.Should().Be("login12");
        top.DisplayName.Should().Be("Name12");
        top.Color.Should().Be("#FF8800");
        top.LastCategory.Should().Be("Game 12");
        top.LastShoutoutAt.Should().Be(new DateTimeOffset(shoutedBefore, TimeSpan.Zero));
        list[1].Color.Should().BeNull("no chat line with a color is on record for them");
        list[1].LastShoutoutAt.Should().BeNull("we never shouted them out");
    }

    [Fact]
    public async Task Leaves_out_broadcaster_bot_zero_archives_shouted_this_stream_and_dismissed()
    {
        Chatter(BroadcasterId, 4);
        Chatter(BotId, 4);
        Chatter("20", 0);
        Chatter("21", 2);
        Chatter("22", 2);
        Chatter("23", 2);
        Chatter("24", 2);
        _db.BotAccounts.Add(
            new()
            {
                IdentityType = "platform",
                Platform = "twitch",
                BotUserId = BotId,
                BotUsername = "thebot",
            }
        );
        _db.ChannelEvents.Add(ShoutoutEvent("22", _clock.GetUtcNow().UtcDateTime.AddMinutes(-10)));
        await _db.SaveChangesAsync();
        _service.Dismiss(Channel, "23").IsSuccess.Should().BeTrue();
        _context.LastShoutoutPerUser["24"] = _clock.GetUtcNow().AddMinutes(-3);

        List<ShoutoutSuggestionDto> list = await SuggestAsync();

        list.Select(s => s.TwitchUserId).Should().Equal("21");
        await _videos
            .DidNotReceive()
            .GetVideosByTwitchUserIdAsync(
                BroadcasterId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Dismissal_lasts_only_for_the_stream_it_was_made_in()
    {
        Chatter("30", 2);
        _service.Dismiss(Channel, "30");
        (await SuggestAsync()).Should().BeEmpty();

        _context.WentLiveAt = _clock.GetUtcNow().AddMinutes(-1);

        (await SuggestAsync()).Select(s => s.TwitchUserId).Should().Equal("30");
    }

    [Fact]
    public async Task A_shoutout_from_an_earlier_stream_does_not_hide_the_chatter()
    {
        Chatter("31", 2);
        _db.ChannelEvents.Add(ShoutoutEvent("31", _clock.GetUtcNow().UtcDateTime.AddHours(-30)));
        await _db.SaveChangesAsync();

        List<ShoutoutSuggestionDto> list = await SuggestAsync();

        list.Should().ContainSingle().Which.LastShoutoutAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_14_day_window_counts_an_archive_made_exactly_14_days_ago_and_not_one_second_older()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        _context.SessionChatters["40"] = "Chat40";
        _archives["40"] = [now.AddDays(-14), now.AddDays(-14).AddSeconds(-1), now.AddDays(-2)];

        List<ShoutoutSuggestionDto> list = await SuggestAsync();

        list.Should().ContainSingle().Which.StreamsLast14Days.Should().Be(2);
    }

    [Fact]
    public async Task Returns_at_most_10_suggestions_keeping_the_ones_with_most_streams()
    {
        for (int i = 0; i < 12; i++)
            Chatter((100 + i).ToString(), 1 + i);

        List<ShoutoutSuggestionDto> list = await SuggestAsync();

        list.Should().HaveCount(10);
        list.Select(s => s.StreamsLast14Days).Should().Equal(12, 11, 10, 9, 8, 7, 6, 5, 4, 3);
    }

    [Fact]
    public async Task An_offline_channel_gets_an_empty_list_and_no_twitch_call()
    {
        Chatter("50", 2);
        _context.IsLive = false;

        (await SuggestAsync()).Should().BeEmpty();

        await _videos
            .DidNotReceiveWithAnyArgs()
            .GetVideosByTwitchUserIdAsync(default!, default, default, default, default!);
    }

    [Fact]
    public async Task Twitch_lookups_are_kept_one_hour_per_user_and_asked_again_after()
    {
        Chatter("60", 2);

        await SuggestAsync();
        await SuggestAsync();
        await _videos
            .Received(1)
            .GetVideosByTwitchUserIdAsync(
                "60",
                "archive",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );

        _clock.Advance(TimeSpan.FromMinutes(61));
        _archives["60"] = [_clock.GetUtcNow().AddDays(-1)];
        List<ShoutoutSuggestionDto> later = await SuggestAsync();

        await _videos
            .Received(2)
            .GetVideosByTwitchUserIdAsync(
                "60",
                "archive",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
        later.Should().ContainSingle().Which.StreamsLast14Days.Should().Be(1);
    }

    [Fact]
    public async Task Channel_information_is_asked_in_one_batch_for_the_suggested_users_only()
    {
        Chatter("70", 2);
        Chatter("71", 0);

        await SuggestAsync();

        await _channels
            .Received(1)
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "70" })),
                Arg.Any<CancellationToken>()
            );
    }

    private static ChannelEvent ShoutoutEvent(string toUserId, DateTime at) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = Channel,
            Type = "bot.shoutout.sent",
            Data = $"{{\"toUserId\":\"{toUserId}\",\"source\":\"pipeline\"}}",
            CreatedAt = at,
        };
}
