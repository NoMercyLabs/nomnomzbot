// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The schedule iCalendar feed exposes TWO auth paths (broadcaster-liveops.md §3.5): the Bearer snapshot
/// (<c>GetScheduleICalendar</c>, Gate-2 <c>live-ops:schedule:read</c>) and a PUBLIC token-query subscription
/// (<c>SubscribeScheduleICalendar</c>) so a calendar app can poll a stable
/// <c>webcal://…/schedule/icalendar/subscribe?token=&lt;OverlayToken&gt;</c> URL without a user session. These
/// tests prove the token path serves the feed for the channel's own token, rejects an absent / unknown token
/// and — critically — a valid token belonging to a DIFFERENT channel, while the Bearer path still serves the
/// feed and keeps its <c>[RequireAction]</c> gate.
/// </summary>
public sealed class LiveOpsScheduleICalendarTests
{
    private const string Ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");

    private static (
        LiveOpsController Controller,
        ITwitchScheduleApi Schedule,
        IChannelService Channels
    ) Build()
    {
        ITwitchScheduleApi schedule = Substitute.For<ITwitchScheduleApi>();
        IChannelService channels = Substitute.For<IChannelService>();
        LiveOpsController controller = new(
            Substitute.For<ITwitchPollsApi>(),
            Substitute.For<ITwitchPredictionsApi>(),
            Substitute.For<ITwitchRaidsApi>(),
            Substitute.For<ITwitchAdsApi>(),
            Substitute.For<ITwitchClipsApi>(),
            schedule,
            Substitute.For<ITwitchStreamsApi>(),
            channels
        );
        return (controller, schedule, channels);
    }

    [Fact]
    public async Task Subscribe_serves_the_ics_for_the_channels_own_overlay_token()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        channels
            .GetByOverlayTokenAsync("good-token", Arg.Any<CancellationToken>())
            .Returns(new ChannelOverlayInfo(Channel.ToString(), "Streamer"));
        schedule
            .GetICalendarAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Ics));

        IActionResult result = await controller.SubscribeScheduleICalendar(
            Channel.ToString(),
            "good-token",
            default
        );

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be(Ics);
        content.ContentType.Should().Be("text/calendar");
    }

    [Fact]
    public async Task Subscribe_rejects_a_token_that_belongs_to_a_different_channel()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        // A real, valid overlay token — but for OtherChannel, not the channel in the route.
        channels
            .GetByOverlayTokenAsync("foreign-token", Arg.Any<CancellationToken>())
            .Returns(new ChannelOverlayInfo(OtherChannel.ToString(), "Someone Else"));

        IActionResult result = await controller.SubscribeScheduleICalendar(
            Channel.ToString(),
            "foreign-token",
            default
        );

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await schedule
            .DidNotReceive()
            .GetICalendarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_rejects_an_unknown_token()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        channels
            .GetByOverlayTokenAsync("bad-token", Arg.Any<CancellationToken>())
            .Returns((ChannelOverlayInfo?)null);

        IActionResult result = await controller.SubscribeScheduleICalendar(
            Channel.ToString(),
            "bad-token",
            default
        );

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await schedule
            .DidNotReceive()
            .GetICalendarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Subscribe_rejects_an_absent_token(string? token)
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();

        IActionResult result = await controller.SubscribeScheduleICalendar(
            Channel.ToString(),
            token,
            default
        );

        result.Should().BeOfType<UnauthorizedObjectResult>();
        await channels
            .DidNotReceive()
            .GetByOverlayTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bearer_snapshot_path_still_serves_the_ics()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, _) = Build();
        schedule
            .GetICalendarAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Ics));

        IActionResult result = await controller.GetScheduleICalendar(Channel.ToString(), default);

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be(Ics);
        content.ContentType.Should().Be("text/calendar");
    }

    [Fact]
    public void Bearer_snapshot_keeps_its_gate_and_the_subscribe_feed_is_public()
    {
        MethodInfo bearer = typeof(LiveOpsController).GetMethod(
            nameof(LiveOpsController.GetScheduleICalendar)
        )!;
        MethodInfo subscribe = typeof(LiveOpsController).GetMethod(
            nameof(LiveOpsController.SubscribeScheduleICalendar)
        )!;

        // Bearer path: Gate-2 gated, never anonymous.
        bearer
            .GetCustomAttribute<RequireActionAttribute>()!
            .ActionKey.Should()
            .Be("live-ops:schedule:read");
        bearer.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();

        // Subscribe path: public (token-in-query), no Gate-2 attribute, distinct route.
        subscribe.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        subscribe.GetCustomAttribute<RequireActionAttribute>().Should().BeNull();
        subscribe
            .GetCustomAttribute<HttpGetAttribute>()!
            .Template.Should()
            .Be("schedule/icalendar/subscribe");
    }
}
