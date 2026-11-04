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

/// <summary>The ad namespace's next, last and snooze timings from Twitch.</summary>
internal static class AdSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        DateTimeOffset now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        ITwitchAdsApi ads = Substitute.For<ITwitchAdsApi>();
        ads.GetAdScheduleAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchAdSchedule(
                        SnoozeCount: 2,
                        SnoozeRefreshAt: (int)now.AddMinutes(34).ToUnixTimeSeconds(),
                        NextAdAt: (int)now.AddMinutes(12).ToUnixTimeSeconds(),
                        Duration: 90,
                        LastAdAt: (int)now.AddMinutes(-48).ToUnixTimeSeconds(),
                        PrerollFreeTime: 1200
                    )
                )
            );
        services.AddSingleton(ads);
    }
}
