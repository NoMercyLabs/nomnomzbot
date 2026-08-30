// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// S052 (widgets-overlays.md §1.2): a system surface is "provisioned for every channel at channel creation
/// (and on first use if missing)" — before this handler, each system surface only ever appeared lazily, the
/// first time a streamer opened its owner page (<c>EnsureSystemWidgetAsync</c>'s "on first use" leg). This
/// handler is the "at channel creation" leg: it hangs off the same <c>ChannelOnboardedEvent</c> fan-out every
/// other onboarding seed job uses, so a brand-new channel already has every system surface the instant
/// onboarding completes, with no dashboard visit required.
/// </summary>
public sealed class SystemWidgetSeedOnOnboardingHandlerTests
{
    private static WidgetDetail SomeWidgetDetail() =>
        new(
            Guid.CreateVersion7(),
            "System Surface",
            "System surface",
            "vue",
            "first_party",
            true,
            "https://overlay/widget",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new(),
            [],
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

    [Fact]
    public async Task Handle_provisions_every_system_surface_for_the_onboarded_channel()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid broadcasterId = Guid.CreateVersion7();
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SomeWidgetDetail()));

        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
            }
        );

        foreach (string naturalKey in FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys)
            await widgets
                .Received(1)
                .EnsureSystemWidgetAsync(
                    broadcasterId.ToString(),
                    naturalKey,
                    Arg.Any<CancellationToken>()
                );
    }

    [Fact]
    public async Task Handle_provisions_the_tts_caption_system_widget()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid broadcasterId = Guid.CreateVersion7();
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SomeWidgetDetail()));

        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
            }
        );

        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_provisions_the_alerts_system_widget()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid broadcasterId = Guid.CreateVersion7();
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "alerts",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SomeWidgetDetail()));

        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
            }
        );

        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "alerts",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_ignores_the_platform_level_sentinel_broadcaster_id()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
            }
        );

        await widgets
            .DidNotReceive()
            .EnsureSystemWidgetAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Handle_swallows_a_failure_from_one_surface_and_still_provisions_the_rest()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid broadcasterId = Guid.CreateVersion7();
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "tts_caption",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<WidgetDetail>("boom", "NOT_FOUND"));
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "alerts",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(SomeWidgetDetail()));

        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        Func<Task> act = () =>
            handler.HandleAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = "12345",
                    Name = "teststreamer",
                }
            );

        await act.Should().NotThrowAsync();
        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                "alerts",
                Arg.Any<CancellationToken>()
            );
    }
}
