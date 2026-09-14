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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The voice-listener page's report call authenticates with the SAME <c>X-Overlay-Token</c> header mechanism
/// as <see cref="OverlayTicketControllerTests"/> — this proves it defers to
/// <see cref="IWidgetService.ResolveBroadcasterIdByOverlayTokenAsync"/> correctly: a missing header, and a
/// token that does not resolve to a channel, are both rejected BEFORE the report ever reaches
/// <see cref="IVoiceTriggerService"/>; a resolved token forwards the resolved channel id (never a client-sent
/// one) and the transcript through untouched.
/// </summary>
public sealed class VoiceTriggerReportControllerTests
{
    private static Api.Controllers.VoiceTriggerReportController Build(
        IWidgetService widgetService,
        IVoiceTriggerService triggers,
        string? tokenHeader
    )
    {
        Api.Controllers.VoiceTriggerReportController controller = new(widgetService, triggers)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        if (tokenHeader != null)
            controller.Request.Headers["X-Overlay-Token"] = tokenHeader;
        return controller;
    }

    [Fact]
    public async Task A_missing_token_header_never_reaches_the_trigger_service()
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        IVoiceTriggerService triggers = Substitute.For<IVoiceTriggerService>();
        Api.Controllers.VoiceTriggerReportController controller = Build(
            widgetService,
            triggers,
            tokenHeader: null
        );

        IActionResult result = await controller.Report(
            new() { Transcript = "technically" },
            CancellationToken.None
        );

        result.Should().BeOfType<UnauthorizedResult>();
        await triggers
            .DidNotReceiveWithAnyArgs()
            .ReportAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unresolvable_token_is_rejected()
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .ResolveBroadcasterIdByOverlayTokenAsync("bad-token", Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        IVoiceTriggerService triggers = Substitute.For<IVoiceTriggerService>();
        Api.Controllers.VoiceTriggerReportController controller = Build(
            widgetService,
            triggers,
            "bad-token"
        );

        IActionResult result = await controller.Report(
            new() { Transcript = "technically" },
            CancellationToken.None
        );

        result.Should().BeOfType<UnauthorizedResult>();
        await triggers
            .DidNotReceiveWithAnyArgs()
            .ReportAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_resolved_token_reports_using_the_RESOLVED_channel_not_a_client_supplied_one()
    {
        Guid resolvedChannel = Guid.NewGuid();
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .ResolveBroadcasterIdByOverlayTokenAsync("good-token", Arg.Any<CancellationToken>())
            .Returns(resolvedChannel);
        IVoiceTriggerService triggers = Substitute.For<IVoiceTriggerService>();
        triggers
            .ReportAsync(resolvedChannel, "well technically", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new VoiceTriggerReportResultDto(true, "technically", 5)));

        Api.Controllers.VoiceTriggerReportController controller = Build(
            widgetService,
            triggers,
            "good-token"
        );

        IActionResult result = await controller.Report(
            new() { Transcript = "well technically" },
            CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        VoiceTriggerReportResultDto value = ok
            .Value.Should()
            .BeOfType<VoiceTriggerReportResultDto>()
            .Subject;
        value.Fired.Should().BeTrue();
        value.NewCount.Should().Be(5);
        await triggers
            .Received(1)
            .ReportAsync(resolvedChannel, "well technically", Arg.Any<CancellationToken>());
    }
}
