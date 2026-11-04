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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

/// <summary>The triggering viewer's stored account, channel activity, wallet and Twitch profile.</summary>
internal static class UserSurfaceFixture
{
    private static readonly Guid ViewerId = Guid.Parse("0192b400-0000-7000-8000-00000000f042");
    private static readonly Guid TargetId = Guid.Parse("0192b400-0000-7000-8000-00000000f046");
    private static readonly Guid HigherRankedId = Guid.Parse(
        "0192b400-0000-7000-8000-00000000f043"
    );
    private static readonly DateTime FollowedAt = new(2025, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    public static async Task SeedDbAsync(ViewerDataTestDbContext database)
    {
        Pronoun pronoun = new()
        {
            Name = "They/Them",
            Key = "theythem",
            Subject = "they",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        };
        database.Set<Pronoun>().Add(pronoun);
        database.Users.Add(
            new User
            {
                Id = ViewerId,
                TwitchUserId = "100000042",
                Username = "sampleviewer",
                UsernameNormalized = "sampleviewer",
                DisplayName = "SampleViewer",
                AccountCreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ProfileRefreshedAt = DateTime.UtcNow,
                Pronoun = pronoun,
            }
        );
        database.Users.Add(
            new User
            {
                Id = TargetId,
                TwitchUserId = "100000043",
                Username = "samplefriend",
                UsernameNormalized = "samplefriend",
                DisplayName = "SampleFriend",
                Pronoun = pronoun,
            }
        );
        await database.SaveChangesAsync();
    }

    public static void Configure(ServiceCollection services, Guid channel)
    {
        IViewerAnalyticsService analytics = Substitute.For<IViewerAnalyticsService>();
        analytics
            .GetProfileAsync(channel, ViewerId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        ViewerId,
                        "100000042",
                        "SampleViewer",
                        new DateTime(2025, 11, 3, 12, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc),
                        45000,
                        1204,
                        0,
                        0,
                        9,
                        true,
                        true,
                        "1000",
                        false,
                        250
                    )
                )
            );
        analytics
            .GetStreakAsync(channel, ViewerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WatchStreakDto(5, 9, new DateOnly(2026, 11, 3))));
        analytics
            .GetProfileAsync(channel, TargetId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        TargetId,
                        "100000043",
                        "SampleFriend",
                        new DateTime(2025, 12, 1, 12, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc),
                        3600,
                        12,
                        0,
                        0,
                        2,
                        true,
                        false,
                        null,
                        false,
                        0
                    )
                )
            );
        analytics
            .GetStreakAsync(channel, TargetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WatchStreakDto(2, 3, new DateOnly(2026, 11, 3))));

        IFollowStateService follows = Substitute.For<IFollowStateService>();
        follows
            .ResolveAsync(channel, "twitch", "100000042", Arg.Any<CancellationToken>())
            .Returns(
                new FollowLookup(
                    FollowState.Following,
                    new DateTimeOffset(FollowedAt, TimeSpan.Zero)
                )
            );

        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .GetBalanceAsync(channel, ViewerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(2500L));
        accounts
            .GetBalanceAsync(channel, TargetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(800L));

        ICurrencyConfigService currency = Substitute.For<ICurrencyConfigService>();
        currency
            .GetConfigAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new CurrencyConfigDto(
                        Guid.Parse("0192b400-0000-7000-8000-00000000f044"),
                        channel,
                        "nomz",
                        null,
                        null,
                        true,
                        0,
                        null,
                        0,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );

        IEconomyLeaderboardService boards = Substitute.For<IEconomyLeaderboardService>();
        Guid boardId = Guid.Parse("0192b400-0000-7000-8000-00000000f045");
        boards
            .ListConfigsAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<LeaderboardConfigDto>>([
                    new LeaderboardConfigDto(
                        boardId,
                        channel,
                        null,
                        "balance",
                        "channel",
                        "all_time",
                        true,
                        100,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    ),
                ])
            );
        boards
            .GetRankingAsync(channel, boardId, Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<LeaderboardEntryDto>>([
                    new LeaderboardEntryDto(1, HigherRankedId, null, "HigherViewer", 3000),
                    new LeaderboardEntryDto(2, ViewerId, null, "SampleViewer", 2500),
                ])
            );

        IQuoteService quotes = Substitute.For<IQuoteService>();
        quotes
            .ListByUserAsync(
                channel,
                ViewerId,
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<QuoteDto>([], 1, 1, 3)));

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new TwitchUser(
                        "100000042",
                        "sampleviewer",
                        "SampleViewer",
                        "",
                        "",
                        "A sample viewer",
                        "https://static.example/viewer.png",
                        "",
                        0,
                        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    ),
                ])
            );

        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.GetUserChatColorsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUserChatColor>>([
                    new TwitchUserChatColor("100000042", "sampleviewer", "SampleViewer", "#1E90FF"),
                ])
            );

        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelFollowerAsync(channel, "100000042", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new TwitchChannelFollower(
                        "100000042",
                        "sampleviewer",
                        "SampleViewer",
                        new DateTimeOffset(FollowedAt, TimeSpan.Zero)
                    )
                )
            );
        channels
            .GetChannelFollowerAsync(channel, "100000043", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<TwitchChannelFollower?>(
                    new TwitchChannelFollower(
                        "100000043",
                        "samplefriend",
                        "SampleFriend",
                        new DateTimeOffset(FollowedAt, TimeSpan.Zero)
                    )
                )
            );
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Channels.Returns(channels);

        services.AddSingleton(analytics);
        services.AddSingleton(follows);
        services.AddSingleton(accounts);
        services.AddSingleton(currency);
        services.AddSingleton(boards);
        services.AddSingleton(quotes);
        services.AddSingleton(users);
        services.AddSingleton(chat);
        services.AddSingleton(channels);
        services.AddSingleton(helix);
        services.AddSingleton<IViewerDirectory, MatrixViewerDirectory>();
    }

    private sealed class MatrixViewerDirectory : IViewerDirectory
    {
        public Task<ViewerRef?> ResolveAsync(
            IServiceProvider services,
            ViewerSide side,
            IReadOnlyDictionary<string, string> seeds,
            CancellationToken cancellationToken
        )
        {
            ViewerRef? who = side switch
            {
                ViewerSide.User when seeds.ContainsKey("user") => new ViewerRef(
                    ViewerId,
                    "100000042",
                    "sampleviewer",
                    "SampleViewer",
                    "twitch"
                ),
                ViewerSide.Target
                    when seeds.TryGetValue("target", out string? target)
                        && target.Contains("samplefriend", StringComparison.OrdinalIgnoreCase) =>
                    new ViewerRef(TargetId, "100000043", "samplefriend", "SampleFriend", "twitch"),
                ViewerSide.Target when seeds.ContainsKey("target") => new ViewerRef(
                    ViewerId,
                    "100000042",
                    "sampleviewer",
                    "SampleViewer",
                    "twitch"
                ),
                _ => null,
            };
            return Task.FromResult(who);
        }
    }
}
