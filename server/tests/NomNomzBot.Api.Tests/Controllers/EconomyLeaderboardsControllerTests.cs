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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the leaderboards controller wires HTTP to <see cref="IEconomyLeaderboardService"/> (economy.md §5):
/// the ranking passes its <c>top</c> override through, the opt-out toggles the route viewer, and a malformed
/// channel id is rejected.
/// </summary>
public sealed class EconomyLeaderboardsControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid Config = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-000000000e03");
    private static readonly Guid Other = Guid.Parse("0192a000-0000-7000-8000-000000000e04");

    private static (
        EconomyLeaderboardsController Controller,
        IEconomyLeaderboardService Service
    ) Build(Guid? caller = null, bool callerMayManageOthers = false)
    {
        IEconomyLeaderboardService service = Substitute.For<IEconomyLeaderboardService>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        Guid actingUser = caller ?? Viewer;
        user.UserId.Returns(actingUser.ToString());
        IActionAuthorizationService authorization = Substitute.For<IActionAuthorizationService>();
        authorization
            .AuthorizeActionAsync(
                actingUser,
                Channel,
                EconomyLeaderboardsController.ManageOthersConsentAction,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(callerMayManageOthers));
        return (new(service, authorization, user), service);
    }

    [Fact]
    public async Task GetRanking_passes_the_top_override_and_returns_ok()
    {
        (EconomyLeaderboardsController controller, IEconomyLeaderboardService service) = Build();
        service
            .GetRankingAsync(Channel, Config, 3, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<LeaderboardEntryDto>>([
                    new(1, Viewer, Guid.NewGuid(), "top", 100),
                ])
            );

        IActionResult result = await controller.GetRanking(Channel.ToString(), Config, 3, default);

        result.Should().BeOfType<OkObjectResult>();
        await service.Received(1).GetRankingAsync(Channel, Config, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptOut_toggles_the_route_viewer()
    {
        (EconomyLeaderboardsController controller, IEconomyLeaderboardService service) = Build();
        service
            .OptOutAsync(Channel, Viewer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await controller.OptOut(Channel.ToString(), Viewer, default);

        result.Should().BeOfType<OkObjectResult>();
        await service.Received(1).OptOutAsync(Channel, Viewer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptOut_of_another_viewer_is_refused_for_a_plain_viewer()
    {
        (EconomyLeaderboardsController controller, IEconomyLeaderboardService service) = Build(
            caller: Other
        );

        IActionResult result = await controller.OptOut(Channel.ToString(), Viewer, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await service
            .DidNotReceive()
            .OptOutAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptIn_of_another_viewer_is_refused_for_a_plain_viewer()
    {
        (EconomyLeaderboardsController controller, IEconomyLeaderboardService service) = Build(
            caller: Other
        );

        IActionResult result = await controller.OptIn(Channel.ToString(), Viewer, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await service
            .DidNotReceive()
            .OptInAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptOut_of_another_viewer_is_allowed_for_a_leaderboard_manager()
    {
        (EconomyLeaderboardsController controller, IEconomyLeaderboardService service) = Build(
            caller: Other,
            callerMayManageOthers: true
        );
        service
            .OptOutAsync(Channel, Viewer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await controller.OptOut(Channel.ToString(), Viewer, default);

        result.Should().BeOfType<OkObjectResult>();
        await service.Received(1).OptOutAsync(Channel, Viewer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListConfigs_rejects_a_malformed_channel_id()
    {
        (EconomyLeaderboardsController controller, _) = Build();

        IActionResult result = await controller.ListConfigs("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
