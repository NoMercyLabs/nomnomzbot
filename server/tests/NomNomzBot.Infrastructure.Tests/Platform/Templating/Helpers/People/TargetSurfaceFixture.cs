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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

/// <summary>The target namespace's live Twitch channel answer for the named person.</summary>
internal static class TargetSurfaceFixture
{
    public static void Configure(ServiceCollection services)
    {
        ITwitchChannelsApi channels = (ITwitchChannelsApi)
            services.Last(d => d.ServiceType == typeof(ITwitchChannelsApi)).ImplementationInstance!;
        channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchChannelInformation>>([
                    .. call.ArgAt<IReadOnlyList<string>>(0)
                        .Select(id => new TwitchChannelInformation(
                            id,
                            id == "100000043" ? "samplefriend" : "sampleviewer",
                            id == "100000043" ? "SampleFriend" : "SampleViewer",
                            "en",
                            "509670",
                            "Just Chatting",
                            "A sample stream",
                            0,
                            [],
                            [],
                            false
                        )),
                ])
            );

        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string id = call.ArgAt<TwitchStreamsFilter>(0).UserIds?.FirstOrDefault() ?? "";
                string login = id == "100000043" ? "samplefriend" : "sampleviewer";
                string display = id == "100000043" ? "SampleFriend" : "SampleViewer";
                return Result.Success(
                    new TwitchPage<TwitchStream>(
                        [
                            new TwitchStream(
                                "target-stream-1",
                                id,
                                login,
                                display,
                                "509670",
                                "Just Chatting",
                                "live",
                                "A sample stream",
                                [],
                                12,
                                new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero),
                                "en",
                                "",
                                false
                            ),
                        ],
                        null,
                        1
                    )
                );
            });
        services.AddSingleton(streams);
    }
}
