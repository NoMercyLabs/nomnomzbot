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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Stream;

/// <summary>The stream namespace's live stats, past broadcast, and category for the cross-surface matrix.</summary>
internal static class StreamSurfaceFixture
{
    private static readonly DateTimeOffset LastEnded = new(2026, 11, 2, 22, 0, 0, TimeSpan.Zero);

    public static void Configure(ServiceCollection services, Guid channel)
    {
        IStreamStatsService stats = Substitute.For<IStreamStatsService>();
        stats
            .GetAsync(
                channel,
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Stats()));

        IChannelAnalyticsService analytics = Substitute.For<IChannelAnalyticsService>();
        analytics
            .ListStreamsAsync(channel, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(History()));
        analytics
            .GetStreamAsync(channel, "stream-previous", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new StreamAnalyticsDto(
                        "stream-previous",
                        "Yesterday's stream",
                        "Just Chatting",
                        LastEnded.AddHours(-3),
                        LastEnded,
                        10800,
                        57,
                        540,
                        25,
                        3,
                        2,
                        1,
                        50,
                        30,
                        []
                    )
                )
            );

        ITwitchGamesApi games = Substitute.For<ITwitchGamesApi>();
        games
            .GetGamesAsync(
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchGame>>([
                    new TwitchGame(
                        "509670",
                        "Just Chatting",
                        "https://static.example/game-{width}x{height}.jpg",
                        "1"
                    ),
                ])
            );

        services.AddSingleton(stats);
        services.AddSingleton(analytics);
        services.AddSingleton(games);
    }

    private static PagedList<StreamListItemDto> History() =>
        new(
            [
                new StreamListItemDto(
                    "stream-1",
                    "Cozy coding night",
                    "Just Chatting",
                    new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.Zero),
                    null,
                    7200,
                    42,
                    800,
                    0,
                    4
                ),
                new StreamListItemDto(
                    "stream-previous",
                    "Yesterday's stream",
                    "Just Chatting",
                    LastEnded.AddHours(-3),
                    LastEnded,
                    10800,
                    57,
                    540,
                    0,
                    3
                ),
            ],
            1,
            100,
            2
        );

    private static StreamStatsDto Stats() =>
        new()
        {
            Stream = new StreamStatsWindowDto { Id = "stream-1" },
            PeakViewers = 57,
            Followers = 6,
            NewFollowers = ["SampleViewer"],
            Subs = new StreamStatsSubsDto
            {
                New = 3,
                Resub = 2,
                Gift = 1,
                Total = 6,
            },
            Gifters = [new() { Name = "SampleViewer", Count = 1 }],
            SelfSubs = ["SampleViewer"],
            Bits = new StreamStatsRankedDto
            {
                Total = 250,
                Top = [new() { Name = "SampleViewer", Count = 250 }],
            },
            RaidsIn = [new() { Name = "SampleRaider", Viewers = 20 }],
            HypeTrain = new StreamStatsHypeTrainDto { Level = 2, Trains = 1 },
            Redemptions = new StreamStatsRankedDto { Total = 4, Top = [] },
            SongRequesters = new StreamStatsRankedDto { Total = 2, Top = [] },
            Chat = new StreamStatsChatDto { Messages = 800, UniqueChatters = 42 },
            TopChatters = [new() { Name = "SampleViewer", Count = 20 }],
            CommandsRun = new StreamStatsRankedDto
            {
                Total = 8,
                Top = [new() { Name = "!hug", Count = 3 }],
            },
        };
}
