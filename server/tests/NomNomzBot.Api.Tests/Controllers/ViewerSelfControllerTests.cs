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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the viewer self routes bind the subject to the token: the service is called with the caller's user id
/// from <see cref="ICurrentUserService"/> (there is no user id on the route), and an unauthenticated call never
/// reaches the service.
/// </summary>
public sealed class ViewerSelfControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000006c01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000006a01");

    private static (ViewerSelfController Controller, IViewerSelfService Service) Build(
        string? userId
    ) => BuildWithActivity(userId, Substitute.For<IViewerSelfActivityService>());

    private static (ViewerSelfController Controller, IViewerSelfService Service) BuildWithActivity(
        string? userId,
        IViewerSelfActivityService activity
    )
    {
        IViewerSelfService service = Substitute.For<IViewerSelfService>();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(userId);
        return (
            new(service, activity, Substitute.For<IViewerSelfQueueService>(), currentUser),
            service
        );
    }

    [Fact]
    public async Task Messages_read_with_the_token_user_and_the_requested_page()
    {
        IViewerSelfActivityService activity = Substitute.For<IViewerSelfActivityService>();
        PagedList<ViewerMessageDto> page = new(
            [new("m1", new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc), "hi", false)],
            2,
            1,
            3
        );
        activity
            .ListMessagesAsync(Channel, Caller, 2, 1, Arg.Any<CancellationToken>())
            .Returns(Result.Success(page));
        (ViewerSelfController controller, _) = BuildWithActivity(Caller.ToString(), activity);

        IActionResult result = await controller.GetMyMessages(
            Channel,
            new() { Page = 2, Take = 1 },
            CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        PaginatedResponse<ViewerMessageDto> body = ok
            .Value.Should()
            .BeOfType<PaginatedResponse<ViewerMessageDto>>()
            .Subject;
        body.Data.Single().Text.Should().Be("hi");
        body.Total.Should().Be(3);
        body.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Commands_read_with_the_token_user()
    {
        IViewerSelfActivityService activity = Substitute.For<IViewerSelfActivityService>();
        activity
            .ListCommandsAsync(Channel, Caller, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ViewerCommandDto>>([]));
        (ViewerSelfController controller, _) = BuildWithActivity(Caller.ToString(), activity);

        IActionResult result = await controller.GetMyCommands(Channel, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await activity.Received(1).ListCommandsAsync(Channel, Caller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_activity_routes_reject_an_unauthenticated_caller()
    {
        IViewerSelfActivityService activity = Substitute.For<IViewerSelfActivityService>();
        (ViewerSelfController controller, _) = BuildWithActivity(null, activity);

        IActionResult messages = await controller.GetMyMessages(
            Channel,
            new(),
            CancellationToken.None
        );
        IActionResult commands = await controller.GetMyCommands(Channel, CancellationToken.None);

        messages.Should().BeOfType<UnauthorizedObjectResult>();
        commands.Should().BeOfType<UnauthorizedObjectResult>();
        activity.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Summary_reads_with_the_token_user_and_returns_the_service_tiles()
    {
        (ViewerSelfController controller, IViewerSelfService service) = Build(Caller.ToString());
        ViewerSelfSummaryDto tiles = new(null, null, 12, 3, 1, true, 40, false, null, "1000", true);
        service
            .GetSummaryAsync(Channel, Caller, Arg.Any<CancellationToken>())
            .Returns(Result.Success(tiles));

        IActionResult result = await controller.GetMySummary(Channel, CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
        await service.Received(1).GetSummaryAsync(Channel, Caller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Watched_channels_reads_with_the_token_user()
    {
        (ViewerSelfController controller, IViewerSelfService service) = Build(Caller.ToString());
        service
            .ListWatchedChannelsAsync(Caller, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WatchedChannelDto>>([]));

        IActionResult result = await controller.GetWatchedChannels(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await service.Received(1).ListWatchedChannelsAsync(Caller, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Both_routes_reject_an_unauthenticated_caller_without_calling_the_service()
    {
        (ViewerSelfController controller, IViewerSelfService service) = Build(null);

        IActionResult watched = await controller.GetWatchedChannels(CancellationToken.None);
        IActionResult summary = await controller.GetMySummary(Channel, CancellationToken.None);

        watched.Should().BeOfType<UnauthorizedObjectResult>().Which.StatusCode.Should().Be(401);
        summary.Should().BeOfType<UnauthorizedObjectResult>().Which.StatusCode.Should().Be(401);
        await service
            .DidNotReceive()
            .ListWatchedChannelsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await service
            .DidNotReceive()
            .GetSummaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
