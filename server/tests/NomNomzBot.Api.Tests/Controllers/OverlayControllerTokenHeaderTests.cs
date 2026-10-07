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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay data endpoints take the overlay token from the <c>X-Overlay-Token</c> header (what the SDK sends,
/// so the token stays out of URLs) and still from <c>?token=</c> (older clients).
/// </summary>
public sealed class OverlayControllerTokenHeaderTests
{
    private static (OverlayController Controller, IWidgetService Service) Create(string? header)
    {
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .GetNowPlayingSnapshotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<OverlayNowPlayingSnapshot?>.Success(null));
        DefaultHttpContext http = new();
        if (header is not null)
            http.Request.Headers["X-Overlay-Token"] = header;
        OverlayController controller = new(service)
        {
            ControllerContext = new() { HttpContext = http },
        };
        return (controller, service);
    }

    [Fact]
    public async Task The_header_token_is_the_token_the_service_resolves_when_there_is_no_query_token()
    {
        (OverlayController controller, IWidgetService service) = Create("header-token");

        IActionResult result = await controller.GetNowPlayingSnapshot(null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await service
            .Received(1)
            .GetNowPlayingSnapshotAsync("header-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_query_token_still_works_for_older_clients_and_wins_over_the_header()
    {
        (OverlayController controller, IWidgetService service) = Create("header-token");

        IActionResult result = await controller.GetNowPlayingSnapshot(
            "query-token",
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();
        await service
            .Received(1)
            .GetNowPlayingSnapshotAsync("query-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_token_in_either_place_is_a_bad_request_and_never_reaches_the_service()
    {
        (OverlayController controller, IWidgetService service) = Create(null);

        IActionResult result = await controller.GetNowPlayingSnapshot(null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        await service
            .DidNotReceiveWithAnyArgs()
            .GetNowPlayingSnapshotAsync(default!, CancellationToken.None);
    }
}
