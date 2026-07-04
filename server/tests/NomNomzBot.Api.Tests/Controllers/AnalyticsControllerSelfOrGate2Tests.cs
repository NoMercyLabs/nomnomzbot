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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the viewer-analytics routes enforce self-or-Gate-2 (analytics.md §5: <c>analytics:viewer:read</c>,
/// self-or-Gate-2) in the action body: a foreign caller without the key is denied and the service is NEVER
/// invoked; the viewer themselves and a manager holding the key both reach the service.
/// </summary>
public sealed class AnalyticsControllerSelfOrGate2Tests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e11");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000e12");
    private static readonly Guid OtherViewer = Guid.Parse("0192a000-0000-7000-8000-000000000e13");

    private sealed record Fixture(
        AnalyticsController Controller,
        IViewerAnalyticsService Viewers,
        IActionAuthorizationService Gate2
    );

    private static Fixture Build(bool gate2Allows)
    {
        IChannelAnalyticsService channelAnalytics = Substitute.For<IChannelAnalyticsService>();
        IViewerAnalyticsService viewerAnalytics = Substitute.For<IViewerAnalyticsService>();
        viewerAnalytics
            .SetAnalyticsOptOutAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        IActionAuthorizationService gate2 = Substitute.For<IActionAuthorizationService>();
        gate2
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(gate2Allows));

        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(Caller.ToString());

        AnalyticsController controller = new(channelAnalytics, viewerAnalytics, gate2, currentUser);
        return new Fixture(controller, viewerAnalytics, gate2);
    }

    [Fact]
    public async Task OptOut_for_another_viewer_without_the_key_is_denied_and_writes_nothing()
    {
        Fixture f = Build(gate2Allows: false);

        IActionResult result = await f.Controller.SetViewerOptOut(
            Channel,
            OtherViewer,
            new SetAnalyticsOptOutRequest(true),
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await f
            .Viewers.DidNotReceive()
            .SetAnalyticsOptOutAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OptOut_for_yourself_writes_without_any_gate2_check()
    {
        Fixture f = Build(gate2Allows: false);

        IActionResult result = await f.Controller.SetViewerOptOut(
            Channel,
            Caller,
            new SetAnalyticsOptOutRequest(true),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await f
            .Viewers.Received(1)
            .SetAnalyticsOptOutAsync(Channel, Caller, true, Arg.Any<CancellationToken>());
        await f
            .Gate2.DidNotReceive()
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OptOut_for_another_viewer_with_the_key_writes_through_gate2()
    {
        Fixture f = Build(gate2Allows: true);

        IActionResult result = await f.Controller.SetViewerOptOut(
            Channel,
            OtherViewer,
            new SetAnalyticsOptOutRequest(false),
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await f
            .Gate2.Received(1)
            .AuthorizeActionAsync(
                Caller,
                Channel,
                "analytics:viewer:read",
                Arg.Any<CancellationToken>()
            );
        await f
            .Viewers.Received(1)
            .SetAnalyticsOptOutAsync(Channel, OtherViewer, false, Arg.Any<CancellationToken>());
    }
}
