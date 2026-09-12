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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S035 item 3 — <c>POST /overlay/ticket</c> exchanges an overlay browser-source token (carried in a header —
/// never a query string) for a short-lived ticket the overlay SDK then uses on the <c>/hubs/overlay</c>
/// WebSocket. Token resolution (a widget's own token, its grace-windowed previous token, or the legacy
/// channel-wide token) is <see cref="IWidgetService.ResolveBroadcasterIdByOverlayTokenAsync"/>'s job (audit B5,
/// covered by <c>WidgetServiceOverlayTokenRotationTests</c>) — this controller only proves it defers to that
/// resolver correctly: a resolved token issues a redeemable ticket, an unresolved one is rejected, and the
/// per-token throttle rejects the (N+1)th request.
/// </summary>
public sealed class OverlayTicketControllerTests
{
    private static Api.Controllers.OverlayTicketController Build(
        IWidgetService widgetService,
        IOverlayTicketService tickets,
        IOverlayConnectionThrottle throttle,
        string? tokenHeader
    )
    {
        Api.Controllers.OverlayTicketController controller = new(widgetService, tickets, throttle)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        if (tokenHeader != null)
            controller.Request.Headers["X-Overlay-Token"] = tokenHeader;
        return controller;
    }

    [Fact]
    public async Task A_valid_token_issues_a_ticket_the_hub_can_redeem()
    {
        Guid broadcasterId = Guid.NewGuid();
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .ResolveBroadcasterIdByOverlayTokenAsync(
                "the-real-overlay-token",
                Arg.Any<CancellationToken>()
            )
            .Returns(broadcasterId);

        OverlayTicketService tickets = new(new FakeTimeProvider());
        OverlayConnectionThrottle throttle = new(new FakeTimeProvider());
        Api.Controllers.OverlayTicketController controller = Build(
            widgetService,
            tickets,
            throttle,
            "the-real-overlay-token"
        );

        IActionResult result = await controller.IssueTicket(CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        string ticket = (string)ok.Value!.GetType().GetProperty("ticket")!.GetValue(ok.Value)!;
        tickets.RedeemTicket(ticket).Should().Be(broadcasterId);
    }

    [Fact]
    public async Task A_missing_token_header_is_rejected()
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        OverlayConnectionThrottle throttle = new(new FakeTimeProvider());
        Api.Controllers.OverlayTicketController controller = Build(
            widgetService,
            tickets,
            throttle,
            tokenHeader: null
        );

        IActionResult result = await controller.IssueTicket(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .ResolveBroadcasterIdByOverlayTokenAsync(
                "not-a-real-token",
                Arg.Any<CancellationToken>()
            )
            .Returns((Guid?)null);

        OverlayTicketService tickets = new(new FakeTimeProvider());
        OverlayConnectionThrottle throttle = new(new FakeTimeProvider());
        Api.Controllers.OverlayTicketController controller = Build(
            widgetService,
            tickets,
            throttle,
            "not-a-real-token"
        );

        IActionResult result = await controller.IssueTicket(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Repeated_requests_from_the_same_token_are_throttled_at_the_Nplus1th_attempt()
    {
        Guid broadcasterId = Guid.NewGuid();
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .ResolveBroadcasterIdByOverlayTokenAsync("hammered-token", Arg.Any<CancellationToken>())
            .Returns(broadcasterId);

        OverlayTicketService tickets = new(new FakeTimeProvider());
        OverlayConnectionThrottle throttle = new(new FakeTimeProvider());

        List<IActionResult> results = [];
        for (int i = 0; i < 11; i++)
        {
            Api.Controllers.OverlayTicketController controller = Build(
                widgetService,
                tickets,
                throttle,
                "hammered-token"
            );
            results.Add(await controller.IssueTicket(CancellationToken.None));
        }

        results.Take(10).Should().AllSatisfy(r => r.Should().BeOfType<OkObjectResult>());
        results[10]
            .Should()
            .BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should()
            .Be(StatusCodes.Status429TooManyRequests);
    }
}
