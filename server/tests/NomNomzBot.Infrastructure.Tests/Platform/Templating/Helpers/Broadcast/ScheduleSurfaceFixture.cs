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
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

/// <summary>The schedule namespace's upcoming Twitch segments and vacation window.</summary>
internal static class ScheduleSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchScheduleApi schedule = Substitute.For<ITwitchScheduleApi>();
        schedule
            .GetScheduleAsync(channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchSchedule(
                        [
                            new TwitchScheduleSegment(
                                "segment-1",
                                new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero),
                                new DateTimeOffset(2026, 11, 3, 22, 0, 0, TimeSpan.Zero),
                                "Bot night",
                                null,
                                new TwitchScheduleCategory("509670", "Just Chatting"),
                                true
                            ),
                            new TwitchScheduleSegment(
                                "segment-2",
                                new DateTimeOffset(2026, 11, 5, 19, 0, 0, TimeSpan.Zero),
                                new DateTimeOffset(2026, 11, 5, 21, 0, 0, TimeSpan.Zero),
                                "Community night",
                                null,
                                new TwitchScheduleCategory("509670", "Just Chatting"),
                                false
                            ),
                        ],
                        "555001",
                        "SampleStreamer",
                        "samplestreamer",
                        new TwitchScheduleVacation(
                            new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 11, 15, 22, 0, 0, TimeSpan.Zero)
                        )
                    )
                )
            );
        services.AddSingleton(schedule);
    }
}
