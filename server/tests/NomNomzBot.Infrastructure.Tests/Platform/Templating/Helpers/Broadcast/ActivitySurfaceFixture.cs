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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

/// <summary>The activity namespace's current chat ring, daily totals and hour grid.</summary>
internal static class ActivitySurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        DateTimeOffset now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        DateOnly today = new(2026, 11, 3);
        IChannelAnalyticsService analytics = (IChannelAnalyticsService)
            services
                .Last(d => d.ServiceType == typeof(IChannelAnalyticsService))
                .ImplementationInstance!;
        analytics
            .GetDailySeriesAsync(channel, today, today, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ChannelAnalyticsDailyDto>>([
                    new ChannelAnalyticsDailyDto(
                        today,
                        42,
                        800,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        null
                    ),
                ])
            );
        analytics
            .GetTopViewersAsync(
                channel,
                TopViewerMetric.Messages,
                today,
                today,
                1,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TopViewerDto>>([
                    new TopViewerDto(
                        Guid.Parse("0192b400-0000-7000-8000-00000000f042"),
                        "SampleViewer",
                        140
                    ),
                ])
            );
        analytics
            .GetChatHoursAsync(channel, today.AddDays(-90), today, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ChannelChatHoursDto(
                        "Europe/Amsterdam",
                        [new ChatHourCellDto(2, 20, 300, 18), new ChatHourCellDto(5, 21, 400, 20)]
                    )
                )
            );

        ITwitchChatAssetsApi assets = (ITwitchChatAssetsApi)
            services
                .Last(d => d.ServiceType == typeof(ITwitchChatAssetsApi))
                .ImplementationInstance!;
        assets
            .GetChattersAsync(channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchPage<TwitchChatter>([], null, 51)));

        ChatActivityRing ring = new(new FakeTimeProvider(now));
        ring.Record(channel, "100000042", "SampleViewer");
        ring.Record(channel, "100000043", "SampleFriend");
        ring.Record(channel, "100000043", "SampleFriend");
        foreach (
            ServiceDescriptor descriptor in services
                .Where(d => d.ImplementationType == typeof(ChatActivityTemplateHelpers))
                .ToList()
        )
            services.Remove(descriptor);
        services.AddSingleton<ITemplateHelperProvider>(sp => new ChatActivityTemplateHelpers(
            sp.GetRequiredService<IServiceScopeFactory>(),
            ring,
            new Random(1)
        ));
    }
}
