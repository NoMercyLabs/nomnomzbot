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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the games controller wires HTTP to <see cref="IGameService"/> + <see cref="IAgeConsentService"/>
/// (economy.md §5): a play binds the game to the route, the player to the caller, and the level to a server-side
/// resolve; granting consent binds the subject to the caller (you confirm your own age) — never the body.
/// </summary>
public sealed class GamesControllerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-000000000f02");
    private static readonly Guid Game = Guid.Parse("0192a000-0000-7000-8000-000000000f03");
    private static readonly Guid Spoofed = Guid.Parse("0192a000-0000-7000-8000-00000000dead");

    private static (
        GamesController Controller,
        IGameService Games,
        IAgeConsentService Age,
        IRoleResolver Roles
    ) Build(bool callerMayReadOthers = false)
    {
        IGameService games = Substitute.For<IGameService>();
        IAgeConsentService age = Substitute.For<IAgeConsentService>();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        user.UserId.Returns(Caller.ToString());
        IActionAuthorizationService authorization = Substitute.For<IActionAuthorizationService>();
        authorization
            .AuthorizeActionAsync(
                Caller,
                Channel,
                GamesController.ReadOthersHistoryAction,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(callerMayReadOthers));
        games
            .GetGameHistoryAsync(
                Arg.Any<Guid>(),
                Arg.Any<GameHistoryFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<GamePlayDto>([], 0, 1, 25)));
        return (new(games, age, roles, authorization, user), games, age, roles);
    }

    [Fact]
    public async Task History_without_a_player_filter_is_scoped_to_the_caller_for_a_plain_viewer()
    {
        (GamesController controller, IGameService games, _, _) = Build();

        IActionResult result = await controller.GetHistory(
            Channel.ToString(),
            new GameHistoryFilter(null, null, null),
            new PageRequestDto(),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await games
            .Received(1)
            .GetGameHistoryAsync(
                Channel,
                Arg.Is<GameHistoryFilter>(f => f.PlayerUserId == Caller),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task History_of_another_player_is_refused_for_a_plain_viewer()
    {
        (GamesController controller, IGameService games, _, _) = Build();

        IActionResult result = await controller.GetHistory(
            Channel.ToString(),
            new GameHistoryFilter(null, Spoofed, null),
            new PageRequestDto(),
            default
        );

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await games
            .DidNotReceive()
            .GetGameHistoryAsync(
                Arg.Any<Guid>(),
                Arg.Any<GameHistoryFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task History_stays_channel_wide_for_a_caller_who_may_read_the_ledger()
    {
        (GamesController controller, IGameService games, _, _) = Build(callerMayReadOthers: true);

        IActionResult result = await controller.GetHistory(
            Channel.ToString(),
            new GameHistoryFilter(null, null, null),
            new PageRequestDto(),
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await games
            .Received(1)
            .GetGameHistoryAsync(
                Channel,
                Arg.Is<GameHistoryFilter>(f => f.PlayerUserId == null),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Play_binds_the_game_to_the_route_player_to_caller_and_level_server_side()
    {
        (GamesController controller, IGameService games, _, IRoleResolver roles) = Build();
        roles
            .ResolveEffectiveLevelAsync(Caller, Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(5));
        games
            .PlayAsync(Channel, Arg.Any<PlayGameRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new GamePlayResultDto(1, "coinflip", "Win", 10, 20, 10, 110, null))
            );

        IActionResult result = await controller.Play(
            Channel.ToString(),
            Game,
            new(Spoofed, Spoofed, 10, 999), // spoofed game, player, level
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await games
            .Received(1)
            .PlayAsync(
                Channel,
                Arg.Is<PlayGameRequest>(p =>
                    p.GameConfigId == Game && p.PlayerUserId == Caller && p.RoleLevel == 5
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GrantConsent_binds_the_subject_to_the_caller()
    {
        (GamesController controller, _, IAgeConsentService age, _) = Build();
        age.GrantAsync(Channel, Arg.Any<GrantAgeConsentRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new AgeConsentDto(Caller, Guid.NewGuid(), true, default, null, "self_confirm")
                )
            );

        IActionResult result = await controller.GrantConsent(
            Channel.ToString(),
            new(Spoofed, "self_confirm", null, null), // spoofed subject
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await age.Received(1)
            .GrantAsync(
                Channel,
                Arg.Is<GrantAgeConsentRequest>(r => r.ViewerUserId == Caller),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ListGames_rejects_a_malformed_channel_id()
    {
        (GamesController controller, _, _, _) = Build();

        IActionResult result = await controller.ListGames("not-a-guid", default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
