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

/// <summary>The charity namespace's active campaign and recent donations.</summary>
internal static class CharitySurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchCharityApi charity = Substitute.For<ITwitchCharityApi>();
        charity
            .GetCharityCampaignAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchCharityCampaign(
                        "campaign-1",
                        "555001",
                        "samplestreamer",
                        "SampleStreamer",
                        "Sample Charity",
                        "A sample charity",
                        "https://static.example/charity.png",
                        "https://www.samplecharity.org",
                        new TwitchCharityAmount(42000, 2, "EUR"),
                        new TwitchCharityAmount(100000, 2, "EUR")
                    )
                )
            );
        charity
            .GetCharityCampaignDonationsAsync(
                channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchCharityDonation>(
                        [
                            new TwitchCharityDonation(
                                "donation-1",
                                "campaign-1",
                                "100000042",
                                "sampleviewer",
                                "SampleViewer",
                                new TwitchCharityAmount(1000, 2, "EUR")
                            ),
                        ],
                        null,
                        17
                    )
                )
            );
        services.AddSingleton(charity);
    }
}
