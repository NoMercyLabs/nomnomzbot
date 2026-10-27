// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Raids;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Stream;

/// <summary>
/// The catalog section 5 stream data served by fakes at the service interfaces. Channel A holds the fixture
/// (live since 12:15Z, 42 viewers, peak 57, last stream ended 2026-10-24T22:00Z); channel B holds different
/// values for every source, so a test can prove the two never mix. Clock 2026-10-26T14:30:00Z, zone
/// Europe/Amsterdam (UTC+1 that day).
/// </summary>
internal sealed class StreamHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 26, 12, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastEnded = new(2026, 10, 24, 22, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public StreamHelperFixture()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(ContextA());
        channels.Get(ChannelB).Returns(ContextB());

        IStreamStatsService stats = Substitute.For<IStreamStatsService>();
        stats
            .GetAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                RequestedMetrics.Add(call.ArgAt<IReadOnlyCollection<string>?>(2)?.ToList() ?? []);
                return Task.FromResult(Result.Success(StatsOf(call.ArgAt<Guid>(0))));
            });

        _services = new ServiceCollection()
            .AddSingleton(stats)
            .AddSingleton(AnalyticsFake())
            .AddSingleton(ChannelsApiFake())
            .AddSingleton(SubscriptionsApiFake())
            .AddSingleton(ModeratorsApiFake())
            .AddSingleton(TeamsApiFake())
            .AddSingleton(VideosApiFake())
            .AddSingleton(GamesApiFake())
            .AddSingleton(UsersApiFake())
            .AddSingleton(RaidHistoryFake())
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            Registry
        );
    }

    /// <summary>The metric lists the stats service was asked for, one entry per call.</summary>
    public List<List<string>> RequestedMetrics { get; } = [];

    public ITemplateHelperRegistry Registry { get; }

    public Task<string> RenderAsync(string template, Guid? broadcasterId) =>
        _resolver.ResolveAsync(template, new Dictionary<string, string>(), broadcasterId);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext ContextA() =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            CurrentStreamId = "40123456789",
            CurrentTitle = "Building a bot",
            CurrentGame = "Software and Game Development",
            WentLiveAt = LiveSince,
            ViewerCount = 42,
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };

    private static ChannelContext ContextB() =>
        new()
        {
            BroadcasterId = ChannelB,
            TwitchChannelId = "654321",
            ChannelName = "otherchannel",
            DisplayName = "OtherChannel",
            IsLive = false,
            CommandPrefix = "?",
            Personality = "Sassy",
            Timezone = "UTC",
            Language = "en-GB",
        };

    private static StreamStatsDto StatsOf(Guid broadcasterId) =>
        broadcasterId == ChannelB
            ? new StreamStatsDto
            {
                Stream = new StreamStatsWindowDto { Id = "b-1" },
                PeakViewers = 9,
                Followers = 1,
                Subs = new StreamStatsSubsDto
                {
                    New = 1,
                    Resub = 0,
                    Gift = 0,
                    Total = 1,
                },
            }
            : new StreamStatsDto
            {
                Stream = new StreamStatsWindowDto { Id = "40123456789" },
                PeakViewers = 57,
                Followers = 6,
                NewFollowers = ["ViewerOne", "ViewerTwo"],
                Subs = new StreamStatsSubsDto
                {
                    New = 3,
                    Resub = 2,
                    Gift = 5,
                    Total = 10,
                },
                Gifters = [new() { Name = "ViewerTwo", Count = 5 }],
                SelfSubs = ["ViewerOne"],
                Bits = new StreamStatsRankedDto
                {
                    Total = 1250,
                    Top = [new() { Name = "ViewerOne", Count = 500 }],
                },
                RaidsIn = [new() { Name = "OtherStreamer", Viewers = 25 }],
                HypeTrain = new StreamStatsHypeTrainDto { Level = 3, Trains = 1 },
                Redemptions = new StreamStatsRankedDto { Total = 14, Top = [] },
                SongRequesters = new StreamStatsRankedDto { Total = 4, Top = [] },
                Chat = new StreamStatsChatDto { Messages = 2430, UniqueChatters = 33 },
                TopChatters =
                [
                    new() { Name = "ViewerOne", Count = 140 },
                    new() { Name = "ViewerTwo", Count = 90 },
                    new() { Name = "ViewerThree", Count = 70 },
                ],
                CommandsRun = new StreamStatsRankedDto
                {
                    Total = 88,
                    Top = [new() { Name = "!hug", Count = 31 }],
                },
            };

    private static IChannelAnalyticsService AnalyticsFake()
    {
        IChannelAnalyticsService analytics = Substitute.For<IChannelAnalyticsService>();
        analytics
            .ListStreamsAsync(
                Arg.Any<Guid>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        call.ArgAt<Guid>(0) == ChannelA ? StreamHistoryA() : StreamHistoryB()
                    )
                )
            );
        analytics
            .GetStreamAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    call.ArgAt<Guid>(0) == ChannelA && call.ArgAt<string>(1) == "s-ended"
                        ? Result.Success(
                            new StreamAnalyticsDto(
                                "s-ended",
                                "Chill Saturday",
                                "Just Chatting",
                                LastEnded.AddSeconds(-15000),
                                LastEnded,
                                15000,
                                61,
                                3102,
                                40,
                                9,
                                4,
                                2,
                                50,
                                30,
                                []
                            )
                        )
                        : Result.Failure<StreamAnalyticsDto>("missing", "NOT_FOUND")
                )
            );
        return analytics;
    }

    private static PagedList<StreamListItemDto> StreamHistoryA()
    {
        List<StreamListItemDto> items =
        [
            Item(
                "40123456789",
                "Building a bot",
                "Software and Game Development",
                LiveSince,
                null,
                8100
            ),
            Item(
                "s-ended",
                "Chill Saturday",
                "Just Chatting",
                LastEnded.AddSeconds(-15000),
                LastEnded,
                15000,
                61,
                3102,
                9
            ),
            Item(
                "s-3",
                "Mining",
                "Minecraft",
                new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero),
                null,
                15000
            ),
        ];
        for (int day = 1; day <= 7; day++)
        {
            items.Add(
                Item(
                    $"s-{day + 10}",
                    "Chat",
                    "Just Chatting",
                    new DateTimeOffset(2026, 10, day + 1, 18, 0, 0, TimeSpan.Zero),
                    null,
                    15000
                )
            );
        }
        items.Add(
            Item(
                "s-18",
                "Long one",
                "Just Chatting",
                new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero),
                null,
                22500
            )
        );
        for (int day = 1; day <= 3; day++)
        {
            items.Add(
                Item(
                    $"s-{day + 20}",
                    "Older",
                    "Minecraft",
                    new DateTimeOffset(2026, 9, day + 10, 18, 0, 0, TimeSpan.Zero),
                    null,
                    15000
                )
            );
        }
        return new PagedList<StreamListItemDto>(items, 1, 100, 214);
    }

    private static PagedList<StreamListItemDto> StreamHistoryB() => new([], 1, 100, 0);

    private static StreamListItemDto Item(
        string id,
        string title,
        string game,
        DateTimeOffset started,
        DateTimeOffset? ended,
        long seconds,
        int? peak = null,
        long messages = 0,
        int followers = 0
    ) => new(id, title, game, started, ended, seconds, peak, messages, 0, followers);

    private static ITwitchChannelsApi ChannelsApiFake()
    {
        ITwitchChannelsApi api = Substitute.For<ITwitchChannelsApi>();
        api.GetChannelFollowerCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(Result.Success(call.ArgAt<Guid>(0) == ChannelA ? 1234 : 5))
            );
        api.GetChannelInformationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        call.ArgAt<Guid>(0) == ChannelA
                            ? new TwitchChannelInformation(
                                "123456",
                                "samplechannel",
                                "SampleChannel",
                                "en",
                                "1",
                                "Software and Game Development",
                                "Building a bot",
                                0,
                                ["English", "Coding"],
                                [],
                                false
                            )
                            : new TwitchChannelInformation(
                                "654321",
                                "otherchannel",
                                "OtherChannel",
                                "nl",
                                "2",
                                "Just Chatting",
                                "Praten",
                                0,
                                ["Nederlands"],
                                ["MatureGame"],
                                true
                            )
                    )
                )
            );
        api.GetChannelEditorsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                        .. Enumerable
                            .Range(1, call.ArgAt<Guid>(0) == ChannelA ? 2 : 1)
                            .Select(i => new TwitchChannelEditor($"e{i}", $"Editor{i}", Now)),
                    ])
                )
            );
        return api;
    }

    private static ITwitchSubscriptionsApi SubscriptionsApiFake()
    {
        ITwitchSubscriptionsApi api = Substitute.For<ITwitchSubscriptionsApi>();
        api.GetSubscriberCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(Result.Success(call.ArgAt<Guid>(0) == ChannelA ? 80 : 2))
            );
        return api;
    }

    private static ITwitchModeratorsApi ModeratorsApiFake()
    {
        ITwitchModeratorsApi api = Substitute.For<ITwitchModeratorsApi>();
        api.GetModeratorsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        new TwitchPage<TwitchModerator>(
                            [
                                .. Enumerable
                                    .Range(1, call.ArgAt<Guid>(0) == ChannelA ? 6 : 1)
                                    .Select(i => new TwitchModerator(
                                        $"m{i}",
                                        $"mod{i}",
                                        $"Mod{i}"
                                    )),
                            ],
                            null,
                            0
                        )
                    )
                )
            );
        api.GetVipsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        new TwitchPage<TwitchVip>(
                            [
                                .. Enumerable
                                    .Range(1, call.ArgAt<Guid>(0) == ChannelA ? 4 : 0)
                                    .Select(i => new TwitchVip($"v{i}", $"Vip{i}", $"vip{i}")),
                            ],
                            null,
                            0
                        )
                    )
                )
            );
        return api;
    }

    private static ITwitchTeamsApi TeamsApiFake()
    {
        ITwitchTeamsApi api = Substitute.For<ITwitchTeamsApi>();
        api.GetChannelTeamsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChannelTeam>>(
                        call.ArgAt<Guid>(0) == ChannelA
                            ?
                            [
                                new TwitchChannelTeam(
                                    "t1",
                                    "sampleteam",
                                    "SampleTeam",
                                    "123456",
                                    "samplechannel",
                                    "SampleChannel",
                                    "",
                                    "",
                                    "",
                                    "",
                                    Now,
                                    Now
                                ),
                            ]
                            : []
                    )
                )
            );
        return api;
    }

    private static ITwitchVideosApi VideosApiFake()
    {
        ITwitchVideosApi api = Substitute.For<ITwitchVideosApi>();
        api.GetVideosByBroadcasterAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        new TwitchPage<TwitchVideo>(
                            call.ArgAt<Guid>(0) == ChannelA
                                ?
                                [
                                    new TwitchVideo(
                                        "123",
                                        "s-ended",
                                        "123456",
                                        "samplechannel",
                                        "SampleChannel",
                                        "Chill Saturday",
                                        "",
                                        LastEnded,
                                        LastEnded,
                                        "https://www.twitch.tv/videos/123",
                                        "",
                                        "public",
                                        10,
                                        "en",
                                        "archive",
                                        "4h10m0s",
                                        null
                                    ),
                                ]
                                : [],
                            null,
                            0
                        )
                    )
                )
            );
        return api;
    }

    private static ITwitchGamesApi GamesApiFake()
    {
        ITwitchGamesApi api = Substitute.For<ITwitchGamesApi>();
        api.GetGamesAsync(
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchGame>>([
                        new TwitchGame(
                            "509670",
                            call.ArgAt<IReadOnlyList<string>?>(1)?.FirstOrDefault() ?? "",
                            "https://static-cdn.jtvnw.net/ttv-boxart/509670-{width}x{height}.jpg",
                            "1"
                        ),
                    ])
                )
            );
        return api;
    }

    private static ITwitchUsersApi UsersApiFake()
    {
        ITwitchUsersApi api = Substitute.For<ITwitchUsersApi>();
        api.GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchUser>>([
                        .. call.ArgAt<IReadOnlyList<string>>(0).Select(UserOfId),
                    ])
                )
            );
        api.GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchUser>>([
                        .. call.ArgAt<IReadOnlyList<string>>(0)
                            .Where(login => login == "otherstreamer")
                            .Select(_ => UserOfId("777")),
                    ])
                )
            );
        return api;
    }

    private static TwitchUser UserOfId(string id) =>
        id switch
        {
            "123456" => new TwitchUser(
                "123456",
                "samplechannel",
                "SampleChannel",
                "",
                "affiliate",
                "",
                "",
                "",
                0,
                new DateTimeOffset(2020, 8, 26, 0, 0, 0, TimeSpan.Zero)
            ),
            "654321" => new TwitchUser(
                "654321",
                "otherchannel",
                "OtherChannel",
                "",
                "",
                "",
                "",
                "",
                0,
                new DateTimeOffset(2025, 10, 26, 0, 0, 0, TimeSpan.Zero)
            ),
            _ => new TwitchUser(
                "777",
                "otherstreamer",
                "OtherStreamer",
                "",
                "",
                "",
                "",
                "",
                0,
                Now
            ),
        };

    private static IRaidHistoryReader RaidHistoryFake()
    {
        IRaidHistoryReader reader = Substitute.For<IRaidHistoryReader>();
        reader
            .GetHistoryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        call.ArgAt<Guid>(0) == ChannelA
                            ? new RaidHistory(
                                new Dictionary<string, RaidStats>
                                {
                                    ["777"] = new(
                                        3,
                                        new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero)
                                    ),
                                },
                                new Dictionary<string, RaidStats> { ["777"] = new(5, LastEnded) }
                            )
                            : new RaidHistory(
                                new Dictionary<string, RaidStats>(),
                                new Dictionary<string, RaidStats>()
                            )
                    )
                )
            );
        return reader;
    }
}
