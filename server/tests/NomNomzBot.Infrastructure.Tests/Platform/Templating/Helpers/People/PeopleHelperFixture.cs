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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

/// <summary>
/// The fixed sample data of catalog section 5 behind faked services. Channel A is the sample channel; channel B
/// holds different values for the same two people, so a test can prove the channels never mix.
/// </summary>
internal sealed class PeopleHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ChannelB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    public static readonly Guid ViewerOne = Guid.Parse("00000000-0000-0000-0000-000000001001");
    public static readonly Guid ViewerTwo = Guid.Parse("00000000-0000-0000-0000-000000001002");
    public static readonly Guid ViewerThree = Guid.Parse("00000000-0000-0000-0000-000000001003");
    public static readonly Guid Streamer = Guid.Parse("00000000-0000-0000-0000-000000001004");

    private static readonly DateTime Epoch = DateTime.UnixEpoch;

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;
    private readonly IChannelRegistry _channelState = Substitute.For<IChannelRegistry>();

    public PeopleHelperFixture()
    {
        _services = new ServiceCollection()
            .AddSingleton<IViewerDirectory, FakeViewerDirectory>()
            .AddSingleton(Analytics())
            .AddSingleton(Follows())
            .AddSingleton(Accounts())
            .AddSingleton(Currency())
            .AddSingleton(Boards())
            .AddSingleton(Quotes())
            .AddSingleton(StoredData())
            .AddSingleton(Users())
            .AddSingleton(Colors())
            .AddSingleton(Channels())
            .AddSingleton(Streams())
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _channelState,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero)),
            Registry
        );
    }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>The seeds of a chat command typed by ViewerOne, naming <paramref name="target"/> as its first argument.</summary>
    public static Dictionary<string, string> Seeds(string? target = "@ViewerTwo")
    {
        Dictionary<string, string> seeds = new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.provider"] = "twitch",
        };
        if (target is not null)
            seeds["target"] = target;
        return seeds;
    }

    public Task<string> RenderAsync(
        string template,
        Guid? broadcasterId,
        Dictionary<string, string>? seeds = null
    ) => _resolver.ResolveAsync(template, seeds ?? Seeds(), broadcasterId);

    /// <summary>Gives <paramref name="broadcasterId"/> a live channel state carrying its own viewer activity steps.</summary>
    public void UseChannelWithViewerSteps(
        Guid broadcasterId,
        int casualFrom,
        int regularFrom,
        int veteranFrom
    ) =>
        _channelState
            .Get(broadcasterId)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = broadcasterId,
                    TwitchChannelId = "9001",
                    ChannelName = "channela",
                    ViewerCasualFrom = casualFrom,
                    ViewerRegularFrom = regularFrom,
                    ViewerVeteranFrom = veteranFrom,
                }
            );

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ViewerProfileDto Profile(
        Guid id,
        string twitchId,
        DateTime lastSeen,
        long watchSeconds,
        bool follower,
        bool sub,
        string? tier,
        long bits
    ) =>
        new(
            id,
            twitchId,
            null,
            new DateTime(2025, 9, 10, 12, 0, 0, DateTimeKind.Utc),
            lastSeen,
            watchSeconds,
            1204,
            0,
            0,
            0,
            follower,
            sub,
            tier,
            false,
            bits
        );

    private static IViewerAnalyticsService Analytics()
    {
        Dictionary<(Guid, Guid), ViewerProfileDto> profiles = new()
        {
            [(ChannelA, ViewerOne)] = Profile(
                ViewerOne,
                "1001",
                new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
                45000,
                true,
                true,
                "1000",
                2300
            ),
            [(ChannelA, ViewerTwo)] = Profile(
                ViewerTwo,
                "1002",
                new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc),
                3600,
                true,
                false,
                null,
                0
            ),
            [(ChannelB, ViewerOne)] = Profile(
                ViewerOne,
                "1001",
                new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                7200,
                false,
                false,
                null,
                10
            ),
        };
        Dictionary<(Guid, Guid), WatchStreakDto> streaks = new()
        {
            [(ChannelA, ViewerOne)] = new WatchStreakDto(5, 9, new DateOnly(2026, 10, 24)),
            [(ChannelA, ViewerTwo)] = new WatchStreakDto(2, 2, new DateOnly(2026, 10, 25)),
            [(ChannelB, ViewerOne)] = new WatchStreakDto(1, 1, new DateOnly(2026, 9, 1)),
        };

        IViewerAnalyticsService analytics = Substitute.For<IViewerAnalyticsService>();
        analytics
            .GetProfileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    profiles.TryGetValue(
                        (call.ArgAt<Guid>(0), call.ArgAt<Guid>(1)),
                        out ViewerProfileDto? p
                    )
                        ? Result.Success(p)
                        : Result.Failure<ViewerProfileDto>("missing", "NOT_FOUND")
                )
            );
        analytics
            .GetStreakAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    streaks.TryGetValue(
                        (call.ArgAt<Guid>(0), call.ArgAt<Guid>(1)),
                        out WatchStreakDto? s
                    )
                        ? Result.Success(s)
                        : Result.Failure<WatchStreakDto>("missing", "NOT_FOUND")
                )
            );
        return analytics;
    }

    private static IFollowStateService Follows()
    {
        IFollowStateService follows = Substitute.For<IFollowStateService>();
        follows
            .ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    call.ArgAt<string>(2) switch
                    {
                        "1001" when call.ArgAt<Guid>(0) == ChannelA => new FollowLookup(
                            FollowState.Following,
                            new DateTimeOffset(2025, 9, 10, 12, 0, 0, TimeSpan.Zero)
                        ),
                        "1002" when call.ArgAt<Guid>(0) == ChannelA => new FollowLookup(
                            FollowState.Following,
                            new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero)
                        ),
                        _ => new FollowLookup(FollowState.NotFollowing, null),
                    }
                )
            );
        return follows;
    }

    private static ICurrencyAccountService Accounts()
    {
        Dictionary<(Guid, Guid), long> balances = new()
        {
            [(ChannelA, ViewerOne)] = 2500,
            [(ChannelA, ViewerTwo)] = 800,
            [(ChannelB, ViewerOne)] = 90,
        };
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .GetBalanceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    balances.TryGetValue((call.ArgAt<Guid>(0), call.ArgAt<Guid>(1)), out long b)
                        ? Result.Success(b)
                        : Result.Failure<long>("missing", "NOT_FOUND")
                )
            );
        return accounts;
    }

    private static ICurrencyConfigService Currency()
    {
        ICurrencyConfigService config = Substitute.For<ICurrencyConfigService>();
        config
            .GetConfigAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        new CurrencyConfigDto(
                            Guid.NewGuid(),
                            call.ArgAt<Guid>(0),
                            call.ArgAt<Guid>(0) == ChannelA ? "nomz" : "coin",
                            call.ArgAt<Guid>(0) == ChannelA ? null : "coins",
                            null,
                            true,
                            0,
                            null,
                            0,
                            Epoch,
                            Epoch
                        )
                    )
                )
            );
        return config;
    }

    private static IEconomyLeaderboardService Boards()
    {
        Dictionary<Guid, LeaderboardEntryDto[]> rankings = new()
        {
            [ChannelA] =
            [
                new(1, Streamer, null, "StreamerFour", 4000),
                new(3, ViewerThree, null, "ViewerThree", 2800),
                new(4, ViewerOne, null, "ViewerOne", 2500),
                new(17, ViewerTwo, null, "ViewerTwo", 800),
            ],
            [ChannelB] =
            [
                new(1, ViewerTwo, null, "ViewerTwo", 500),
                new(2, ViewerOne, null, "ViewerOne", 90),
            ],
        };
        IEconomyLeaderboardService boards = Substitute.For<IEconomyLeaderboardService>();
        boards
            .ListConfigsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<LeaderboardConfigDto>>([
                        new LeaderboardConfigDto(
                            Guid.NewGuid(),
                            call.ArgAt<Guid>(0),
                            null,
                            "balance",
                            "channel",
                            "all_time",
                            true,
                            100,
                            Epoch,
                            Epoch
                        ),
                    ])
                )
            );
        boards
            .GetRankingAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<LeaderboardEntryDto>>(
                        rankings[call.ArgAt<Guid>(0)]
                    )
                )
            );
        return boards;
    }

    private static IQuoteService Quotes()
    {
        IQuoteService quotes = Substitute.For<IQuoteService>();
        quotes
            .ListByUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success(
                        new PagedList<QuoteDto>(
                            [],
                            1,
                            1,
                            call.ArgAt<Guid>(0) == ChannelA && call.ArgAt<Guid>(1) == ViewerOne
                                ? 3
                                : 1
                        )
                    )
                )
            );
        return quotes;
    }

    private static IViewerDataService StoredData()
    {
        IViewerDataService data = Substitute.For<IViewerDataService>();
        data.GetAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success<string?>(
                        call.ArgAt<Guid>(0) == ChannelA
                        && call.ArgAt<Guid>(1) == ViewerOne
                        && call.ArgAt<string>(2) == "color"
                            ? "blue"
                            : null
                    )
                )
            );
        return data;
    }

    private static TwitchUser TwitchPerson(
        string id,
        string login,
        string display,
        string bio,
        DateTimeOffset created
    ) =>
        new(
            id,
            login,
            display,
            "",
            "",
            bio,
            $"https://static-cdn.jtvnw.net/jtv_user_pictures/{login}.png",
            "",
            0,
            created
        );

    private static ITwitchUsersApi Users()
    {
        List<TwitchUser> people =
        [
            TwitchPerson(
                "1001",
                "viewerone",
                "ViewerOne",
                "I like bots",
                new(2022, 8, 14, 12, 0, 0, TimeSpan.Zero)
            ),
            TwitchPerson(
                "1002",
                "viewertwo",
                "ViewerTwo",
                "I build castles",
                new(2019, 5, 2, 12, 0, 0, TimeSpan.Zero)
            ),
        ];
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchUser>>([
                        .. people.Where(p => call.ArgAt<IReadOnlyList<string>>(0).Contains(p.Id)),
                    ])
                )
            );
        return users;
    }

    private static ITwitchChatApi Colors()
    {
        Dictionary<string, string> colors = new() { ["1001"] = "#1E90FF", ["1002"] = "#FF4500" };
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.GetUserChatColorsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchUserChatColor>>([
                        .. call.ArgAt<IReadOnlyList<string>>(0)
                            .Where(colors.ContainsKey)
                            .Select(id => new TwitchUserChatColor(id, "", "", colors[id])),
                    ])
                )
            );
        return chat;
    }

    private static ITwitchChannelsApi Channels()
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    Result.Success<IReadOnlyList<TwitchChannelInformation>>([
                        .. call.ArgAt<IReadOnlyList<string>>(0)
                            .Where(id => id is "1002" or "1004")
                            .Select(id => new TwitchChannelInformation(
                                id,
                                "",
                                "",
                                "en",
                                "1",
                                "Minecraft",
                                "Building castles",
                                0,
                                [],
                                [],
                                false
                            )),
                    ])
                )
            );
        return channels;
    }

    private static ITwitchStreamsApi Streams()
    {
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                bool live = call.ArgAt<TwitchStreamsFilter>(0).UserIds!.Contains("1004");
                IReadOnlyList<TwitchStream> items = live
                    ?
                    [
                        new TwitchStream(
                            "s1",
                            "1004",
                            "streamerfour",
                            "StreamerFour",
                            "1",
                            "Minecraft",
                            "live",
                            "Building castles",
                            [],
                            18,
                            DateTimeOffset.UnixEpoch,
                            "en",
                            "",
                            false
                        ),
                    ]
                    : [];
                return Task.FromResult(
                    Result.Success(new TwitchPage<TwitchStream>(items, null, items.Count))
                );
            });
        return streams;
    }
}

/// <summary>Maps the seeds of the sample people to references, with no database.</summary>
internal sealed class FakeViewerDirectory : IViewerDirectory
{
    public Task<ViewerRef?> ResolveAsync(
        IServiceProvider services,
        ViewerSide side,
        IReadOnlyDictionary<string, string> seeds,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            side == ViewerSide.User
                ? seeds.ContainsKey("user.id")
                    ? new ViewerRef(
                        PeopleHelperFixture.ViewerOne,
                        "1001",
                        "viewerone",
                        "ViewerOne",
                        "twitch"
                    )
                    : null
                : Target(seeds.GetValueOrDefault("target")?.TrimStart('@'))
        );

    private static ViewerRef? Target(string? typed) =>
        typed switch
        {
            null or "" => null,
            "ViewerTwo" => new ViewerRef(
                PeopleHelperFixture.ViewerTwo,
                "1002",
                "viewertwo",
                "ViewerTwo",
                "twitch"
            ),
            "StreamerFour" => new ViewerRef(
                PeopleHelperFixture.Streamer,
                "1004",
                "streamerfour",
                "StreamerFour",
                "twitch"
            ),
            _ => new ViewerRef(null, null, typed.ToLowerInvariant(), typed, "twitch"),
        };
}
