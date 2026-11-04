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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Platforms;

/// <summary>The platform namespace's connected Twitch channel and stream-to-date analytics.</summary>
internal static class PlatformSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        IPlatformDirectory directory = Substitute.For<IPlatformDirectory>();
        directory
            .ListAsync(Arg.Any<IServiceProvider>(), channel, Arg.Any<CancellationToken>())
            .Returns(
                new List<PlatformLink> { new("twitch", channel, "555001", "samplestreamer", true) }
            );

        IChannelAnalyticsService analytics = (IChannelAnalyticsService)
            services
                .Last(d => d.ServiceType == typeof(IChannelAnalyticsService))
                .ImplementationInstance!;
        analytics
            .GetPlatformSummaryAsync(
                channel,
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<ChannelAnalyticsPlatformSummaryDto>>([
                    new ChannelAnalyticsPlatformSummaryDto("twitch", 800, 42, 4, 2, 100),
                ])
            );

        foreach (
            ServiceDescriptor descriptor in services
                .Where(d => d.ImplementationType == typeof(PlatformTemplateHelpers))
                .ToList()
        )
            services.Remove(descriptor);
        services.AddSingleton<ITemplateHelperProvider>(sp => new PlatformTemplateHelpers(
            sp.GetRequiredService<IServiceScopeFactory>(),
            directory
        ));
    }
}
