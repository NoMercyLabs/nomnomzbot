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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Bot;

/// <summary>The bot namespace's process clock, commands, timers and moderator standing.</summary>
internal static class BotSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        IBotIdentityDirectory identity = Substitute.For<IBotIdentityDirectory>();
        identity
            .NameAsync(Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns("SampleBot");

        IBuiltinCommandService commands = Substitute.For<IBuiltinCommandService>();
        commands
            .ListAsync(channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    new BuiltinCommandDto(
                        "so",
                        "!so",
                        true,
                        15,
                        0,
                        "Everyone",
                        "so",
                        false,
                        false,
                        false,
                        null,
                        null,
                        null,
                        0
                    ),
                ])
            );

        ITimerManagementService timers = Substitute.For<ITimerManagementService>();
        timers
            .ListAsync(
                channel.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new PagedList<TimerListItem>(
                        [
                            new TimerListItem(
                                Guid.Parse("0192b400-0000-7000-8000-00000000f049"),
                                "sample timer",
                                10,
                                true,
                                false,
                                null,
                                1,
                                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
                            ),
                        ],
                        1,
                        100,
                        1
                    )
                )
            );

        IBotModeratorStatusReader moderator = Substitute.For<IBotModeratorStatusReader>();
        moderator
            .ReadAsync(channel, Arg.Any<CancellationToken>())
            .Returns(new BotModeratorReading(BotModeratorStanding.Moderator, null, null));

        services.AddSingleton(commands);
        services.AddSingleton(timers);
        services.AddSingleton(moderator);
        foreach (
            ServiceDescriptor descriptor in services
                .Where(d => d.ImplementationType == typeof(BotTemplateHelpers))
                .ToList()
        )
            services.Remove(descriptor);
        services.AddSingleton<ITemplateHelperProvider>(sp => new BotTemplateHelpers(
            sp.GetRequiredService<IServiceScopeFactory>(),
            identity,
            new MatrixProcessInfo()
        ));
    }

    private sealed class MatrixProcessInfo : IBotProcessInfo
    {
        public DateTimeOffset StartedAt { get; } =
            new DateTimeOffset(2026, 10, 31, 14, 0, 0, TimeSpan.Zero);

        public string Version => "1.42.0";
    }
}
