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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

/// <summary>The Hype Train namespace's current train and record.</summary>
internal static class HypeTrainSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        DateTimeOffset now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        ITwitchHypeTrainApi trains = Substitute.For<ITwitchHypeTrainApi>();
        trains
            .GetHypeTrainStatusAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchHypeTrainStatus(
                        new TwitchHypeTrain(
                            "train-1",
                            "555001",
                            "samplestreamer",
                            "SampleStreamer",
                            3,
                            5400,
                            1800,
                            3000,
                            [
                                new TwitchHypeTrainContribution(
                                    "100000042",
                                    "sampleviewer",
                                    "SampleViewer",
                                    "BITS",
                                    300
                                ),
                            ],
                            null,
                            now.AddMinutes(-10),
                            now.AddMinutes(4),
                            "regular",
                            false
                        ),
                        new TwitchHypeTrainRecord(5, 9000, now.AddDays(-30)),
                        null
                    )
                )
            );
        services.AddSingleton(trains);
    }
}
