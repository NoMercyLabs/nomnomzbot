// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Stream.ShoutoutSuggestions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Every Get Videos lookup reads the bot token through one scoped DbContext, which is not thread-safe.
/// The lookups must therefore run one after another, never at the same time.
/// </summary>
public sealed class ShoutoutSuggestionServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000a5002");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-10-13T20:00:00Z"));

    public ShoutoutSuggestionServiceTests()
    {
        _connection.Open();
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Video_lookups_never_overlap_and_the_suggestions_are_ordered_by_stream_count()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        ChannelContext context = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "1",
            ChannelName = "streamer",
            IsLive = true,
            WentLiveAt = now.AddHours(-1),
        };
        string[] ids = ["101", "102", "103", "104", "105", "106", "107"];
        foreach (string id in ids)
            context.SessionChatters[id] = "Chat" + id;

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(context);

        int inFlight = 0;
        int maxInFlight = 0;
        object gate = new();
        ITwitchVideosApi videos = Substitute.For<ITwitchVideosApi>();
        videos
            .GetVideosByTwitchUserIdAsync(
                Arg.Any<string>(),
                "archive",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                string user = call.ArgAt<string>(0);
                lock (gate)
                {
                    inFlight++;
                    maxInFlight = Math.Max(maxInFlight, inFlight);
                }

                await Task.Delay(40);
                lock (gate)
                    inFlight--;

                int count = int.Parse(user) - 100;
                List<TwitchVideo> items = Enumerable
                    .Range(0, count)
                    .Select(i => Video(user, now.AddDays(-1 - i)))
                    .ToList();
                return Result.Success(new TwitchPage<TwitchVideo>(items, null, items.Count));
            });

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
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

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        ShoutoutSuggestionService service = new(
            registry,
            _db,
            videos,
            channels,
            users,
            new ShoutoutSuggestionState(),
            _clock
        );

        Result<IReadOnlyList<ShoutoutSuggestionDto>> result = await service.GetAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        maxInFlight
            .Should()
            .Be(1, "the lookups share one scoped DbContext, which is not thread-safe");
        result
            .Value.Select(s => s.TwitchUserId)
            .Should()
            .Equal("107", "106", "105", "104", "103", "102", "101");
        result.Value.Select(s => s.StreamsLast30Days).Should().Equal(7, 6, 5, 4, 3, 2, 1);
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
}
