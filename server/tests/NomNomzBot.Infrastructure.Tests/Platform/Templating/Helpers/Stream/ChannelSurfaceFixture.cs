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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Raids;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Stream;

/// <summary>The channel namespace's Twitch profile, staff, VOD and raid answers.</summary>
internal static class ChannelSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchChannelsApi channels = (ITwitchChannelsApi)
            services.Last(d => d.ServiceType == typeof(ITwitchChannelsApi)).ImplementationInstance!;
        channels
            .GetChannelFollowerCountAsync(channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(1234));
        channels
            .GetChannelInformationAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChannelInformation(
                        "555001",
                        "samplestreamer",
                        "SampleStreamer",
                        "en",
                        "509670",
                        "Just Chatting",
                        "Cozy coding night",
                        0,
                        ["English", "Coding"],
                        ["MatureGame"],
                        true
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                    new TwitchChannelEditor(
                        "100000042",
                        "SampleViewer",
                        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    ),
                ])
            );

        ITwitchSubscriptionsApi subs = Substitute.For<ITwitchSubscriptionsApi>();
        subs.GetSubscriberCountAsync(channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(80));

        ITwitchModeratorsApi staff = Substitute.For<ITwitchModeratorsApi>();
        staff
            .GetModeratorsAsync(channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("100000042", "sampleviewer", "SampleViewer")],
                        null,
                        1
                    )
                )
            );
        staff
            .GetVipsAsync(channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchVip>(
                        [new TwitchVip("100000043", "samplefriend", "SampleFriend")],
                        null,
                        1
                    )
                )
            );

        ITwitchTeamsApi teams = Substitute.For<ITwitchTeamsApi>();
        teams
            .GetChannelTeamsAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelTeam>>([
                    new TwitchChannelTeam(
                        "team-1",
                        "sampleteam",
                        "SampleTeam",
                        "555001",
                        "samplestreamer",
                        "SampleStreamer",
                        "",
                        "",
                        "",
                        "",
                        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    ),
                ])
            );

        ITwitchVideosApi videos = Substitute.For<ITwitchVideosApi>();
        videos
            .GetVideosByBroadcasterAsync(
                channel,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchVideo>(
                        [
                            new TwitchVideo(
                                "video-1",
                                "stream-previous",
                                "555001",
                                "samplestreamer",
                                "SampleStreamer",
                                "Yesterday's stream",
                                "",
                                new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero),
                                new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero),
                                "https://www.twitch.tv/videos/video-1",
                                "",
                                "public",
                                10,
                                "en",
                                "archive",
                                "3h0m0s",
                                null
                            ),
                        ],
                        null,
                        1
                    )
                )
            );

        ITwitchUsersApi users = (ITwitchUsersApi)
            services.Last(d => d.ServiceType == typeof(ITwitchUsersApi)).ImplementationInstance!;
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    .. call.ArgAt<IReadOnlyList<string>>(0).Select(UserById),
                ])
            );
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    .. call.ArgAt<IReadOnlyList<string>>(0)
                        .Where(login =>
                            login.Equals("sampleraider", StringComparison.OrdinalIgnoreCase)
                        )
                        .Select(_ => UserById("777")),
                ])
            );

        IRaidHistoryReader raids = Substitute.For<IRaidHistoryReader>();
        DateTimeOffset raidedAt = new(2026, 11, 2, 22, 0, 0, TimeSpan.Zero);
        raids
            .GetHistoryAsync(channel, "555001", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new RaidHistory(
                        new Dictionary<string, RaidStats> { ["777"] = new(2, raidedAt) },
                        new Dictionary<string, RaidStats> { ["777"] = new(3, raidedAt) }
                    )
                )
            );

        IYouTubeMembersService members = Substitute.For<IYouTubeMembersService>();
        members
            .GetMemberCountAsync(channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(12));

        services.AddSingleton(subs);
        services.AddSingleton(members);
        services.AddSingleton(staff);
        services.AddSingleton(teams);
        services.AddSingleton(videos);
        services.AddSingleton(raids);
    }

    private static TwitchUser UserById(string id) =>
        id switch
        {
            "555001" => new TwitchUser(
                id,
                "samplestreamer",
                "SampleStreamer",
                "",
                "affiliate",
                "",
                "",
                "",
                0,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
            ),
            "777" => new TwitchUser(
                id,
                "sampleraider",
                "SampleRaider",
                "",
                "",
                "",
                "",
                "",
                0,
                new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero)
            ),
            "2002" => new TwitchUser(
                id,
                "otherstreamer",
                "OtherStreamer",
                "",
                "",
                "",
                "",
                "",
                0,
                new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero)
            ),
            _ => new TwitchUser(
                id,
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
        };
}
