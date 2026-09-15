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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// S-OBS-06: proves <c>SoundClipService.StopAsync</c> — the backend half of the dashboard's Stop control and
/// the REST endpoint behind it — pushes a real stop through <see cref="ISoundClipOverlayNotifier"/> scoped to
/// the resolved tenant ONLY (a stop for channel A must never reach channel B's overlay), and reports a
/// not-attached failure instead of a silent, misleading success when no overlay is currently connected.
/// </summary>
public sealed class SoundClipServiceStopTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192c000-0000-7000-8000-00000000e001");
    private static readonly Guid ChannelB = Guid.Parse("0192c000-0000-7000-8000-00000000e002");

    private static (
        SoundClipService Service,
        ISoundClipOverlayNotifier Overlay,
        IOverlayPresenceRegistry Presence
    ) Build()
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ISoundClipOverlayNotifier overlay = Substitute.For<ISoundClipOverlayNotifier>();
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();

        SoundClipService service = new(
            db,
            Substitute.For<ISoundClipStore>(),
            overlay,
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IResourceQuotaService>(),
            Substitute.For<IPipelineStepReferenceScanner>(),
            presence
        );
        return (service, overlay, presence);
    }

    [Fact]
    public async Task StopAsync_with_an_overlay_connected_pushes_a_stop_all_to_that_channel_only()
    {
        (
            SoundClipService service,
            ISoundClipOverlayNotifier overlay,
            IOverlayPresenceRegistry presence
        ) = Build();
        presence.IsOverlayConnected(ChannelA).Returns(true);

        Result result = await service.StopAsync(ChannelA);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await overlay
            .Received(1)
            .StopSoundAsync(ChannelA, null, true, Arg.Any<CancellationToken>());
        // Two-tenant check: the other channel's overlay must never see this stop.
        await overlay
            .DidNotReceive()
            .StopSoundAsync(
                ChannelB,
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task StopAsync_with_no_overlay_connected_fails_not_attached_and_never_touches_the_overlay()
    {
        (
            SoundClipService service,
            ISoundClipOverlayNotifier overlay,
            IOverlayPresenceRegistry presence
        ) = Build();
        presence.IsOverlayConnected(ChannelA).Returns(false);

        Result result = await service.StopAsync(ChannelA);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_ATTACHED");
        await overlay
            .DidNotReceive()
            .StopSoundAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }
}
