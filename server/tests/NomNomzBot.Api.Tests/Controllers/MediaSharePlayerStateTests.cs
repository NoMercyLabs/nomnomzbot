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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.MediaShare.Playback;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>The dashboard reads the player state the overlay last reported, so a fresh page shows it.</summary>
public sealed class MediaSharePlayerStateTests
{
    private static readonly Guid Channel = Guid.Parse("019f2d00-2222-7000-8000-000000000001");

    private static (MediaShareController Controller, MediaPlayerStateStore Store) Build()
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        MediaPlayerStateStore store = new();
        IMediaShareService queue = Substitute.For<IMediaShareService>();
        MediaSharePlaybackService playback = new(
            queue,
            store,
            Substitute.For<IEventBus>(),
            TimeProvider.System
        );
        MediaShareController controller = new(
            queue,
            playback,
            tenant,
            Substitute.For<ICurrentUserService>()
        );
        return (controller, store);
    }

    private static MediaPlayerStateDto Body(IActionResult result) =>
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<MediaPlayerStateDto>>()
            .Which.Data!;

    [Fact]
    public void A_channel_the_overlay_never_reported_reads_idle_at_full_volume()
    {
        (MediaShareController controller, MediaPlayerStateStore _) = Build();

        MediaPlayerStateDto state = Body(controller.GetPlayer());

        state.Should().Be(MediaPlayerStateDto.Idle);
    }

    [Fact]
    public void The_read_returns_the_item_position_length_and_volume_the_overlay_reported()
    {
        (MediaShareController controller, MediaPlayerStateStore store) = Build();
        Guid requestId = Guid.Parse("019f2d00-2222-7000-8000-000000000099");
        MediaPlayerStateDto reported = new(
            MediaPlayerStates.Paused,
            requestId,
            "Stoneys Spaghetti Code",
            "viewer",
            "https://i.ytimg.com/vi/x/hq.jpg",
            12,
            26,
            40
        );
        store.Set(Channel, reported);

        MediaPlayerStateDto state = Body(controller.GetPlayer());

        state.Should().Be(reported);
    }
}
