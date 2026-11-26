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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A Raid Recap test showed no avatar in the bottom-left card: the sample named a made-up channel with no picture,
/// and the widget's own stats read 404s on the sample's stream, so it kept the sample. A test now shows the
/// channel's own name and profile picture, as a real raid does.
/// </summary>
public sealed class RaidRecapTestEventChannelTests
{
    private const string Avatar =
        "https://static-cdn.jtvnw.net/jtv_user_pictures/stoney-profile_image-300x300.png";

    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static ApiTestDbContext SeedChannel()
    {
        ApiTestDbContext db = ApiTestDbContext.New();
        db.Users.Add(
            new User
            {
                Id = Owner,
                Username = "stoney_eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
                ProfileImageUrl = Avatar,
            }
        );
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Owner,
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task A_recap_test_shows_the_channels_own_name_and_avatar()
    {
        using ApiTestDbContext db = SeedChannel();
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = "Raid Recap",
            EventSubscriptions = ["raid_recap.show"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        await controller.Fire(
            Broadcaster.ToString(),
            widget.Id.ToString(),
            new WidgetTestEventRequest("raid_recap.show", null),
            CancellationToken.None
        );

        WidgetEventDto pushed = (WidgetEventDto)
            notifier.ReceivedCalls().Single().GetArguments()[2]!;
        StreamStatsDto recap = pushed.Data.Should().BeOfType<StreamStatsDto>().Subject;
        recap
            .Channel.Should()
            .BeEquivalentTo(
                new StreamStatsChannelDto { Name = "Stoney_Eagle", AvatarUrl = Avatar }
            );
    }

    [Fact]
    public async Task The_editor_preview_recap_shows_the_channels_own_name_and_avatar()
    {
        using ApiTestDbContext db = SeedChannel();
        WidgetTestEventController controller = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );

        IActionResult result = await controller.Samples(
            Broadcaster.ToString(),
            CancellationToken.None
        );

        Dictionary<string, object> table = (
            (StatusResponseDto<Dictionary<string, object>>)((ObjectResult)result).Value!
        ).Data!;
        StreamStatsDto recap = table["raid_recap.show"].Should().BeOfType<StreamStatsDto>().Subject;
        recap
            .Channel.Should()
            .BeEquivalentTo(
                new StreamStatsChannelDto { Name = "Stoney_Eagle", AvatarUrl = Avatar }
            );
    }
}
