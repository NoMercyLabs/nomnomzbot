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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Contract guard for the live-game session reads (live-games.md §5). <c>GetActive</c> and <c>GetCatalog</c>
/// return <see cref="IActionResult"/>, so the ONLY thing that puts their DTO schemas
/// (<see cref="GameSessionDto"/>, <see cref="LiveGameCatalogEntryDto"/>) into <c>openapi/v1.json</c> — and
/// therefore lets the Kotlin client contract-guard them — is the typed
/// <c>[ProducesResponseType&lt;StatusResponseDto&lt;T&gt;&gt;(200)]</c> attribute the ApiExplorer reads. A dropped
/// or mis-typed attribute silently drops the schema; these tests fail when that happens. The behavioural cases
/// prove the same actions still return the typed payload with the right shape.
/// </summary>
public sealed class GameSessionsControllerContractTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid Session = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");

    private static (
        GameSessionsController Controller,
        ILiveGameEngine Engine,
        ILiveGameCatalog Catalog
    ) Build()
    {
        ILiveGameEngine engine = Substitute.For<ILiveGameEngine>();
        ILiveGameCatalog catalog = Substitute.For<ILiveGameCatalog>();
        ICurrentUserService user = Substitute.For<ICurrentUserService>();
        return (new GameSessionsController(engine, catalog, user), engine, catalog);
    }

    private static Type? ProducedTypeFor(string methodName, int statusCode)
    {
        MethodInfo method =
            typeof(GameSessionsController).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance
            ) ?? throw new MissingMethodException(nameof(GameSessionsController), methodName);

        return method
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .FirstOrDefault(a => a.StatusCode == statusCode)
            ?.Type;
    }

    [Fact]
    public void GetActive_declares_the_typed_200_schema_so_openapi_emits_GameSessionDto()
    {
        ProducedTypeFor(nameof(GameSessionsController.GetActive), 200)
            .Should()
            .Be(typeof(StatusResponseDto<GameSessionDto>));
    }

    [Fact]
    public void GetCatalog_declares_the_typed_200_schema_so_openapi_emits_the_catalog_entry()
    {
        ProducedTypeFor(nameof(GameSessionsController.GetCatalog), 200)
            .Should()
            .Be(typeof(StatusResponseDto<IReadOnlyList<LiveGameCatalogEntryDto>>));
    }

    [Fact]
    public async Task GetActive_returns_the_active_session_wrapped_in_the_typed_envelope()
    {
        (GameSessionsController controller, ILiveGameEngine engine, _) = Build();
        GameSessionDto active = new(
            Session,
            "raffle",
            "lobby",
            3,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(2),
            null,
            null,
            null
        );
        engine
            .GetActiveAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(active));

        IActionResult result = await controller.GetActive(Channel.ToString(), default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<GameSessionDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<GameSessionDto>>()
            .Subject;
        body.Data.Should().Be(active);
    }

    [Fact]
    public void GetCatalog_maps_every_discovered_manifest_into_the_typed_catalog_entry()
    {
        (GameSessionsController controller, _, ILiveGameCatalog catalog) = Build();
        LiveGameManifest manifest = new(
            DisplayName: "Raffle",
            InputKeywords: ["join", "enter"],
            OverlayWidgetKey: "raffle-overlay",
            MinPlayers: 2,
            MaxPlayers: 50,
            LobbyWindow: TimeSpan.FromSeconds(90),
            TickInterval: TimeSpan.FromSeconds(5),
            RequiresEntryFee: true
        );
        catalog.All.Returns(new Dictionary<string, LiveGameManifest> { ["raffle"] = manifest });

        IActionResult result = controller.GetCatalog(Channel.ToString());

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<LiveGameCatalogEntryDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<LiveGameCatalogEntryDto>>>()
            .Subject;

        body.Data.Should().ContainSingle();
        LiveGameCatalogEntryDto entry = body.Data!.Single();
        entry.GameKey.Should().Be("raffle");
        entry.DisplayName.Should().Be("Raffle");
        entry.InputKeywords.Should().Equal("join", "enter");
        entry.OverlayWidgetKey.Should().Be("raffle-overlay");
        entry.MinPlayers.Should().Be(2);
        entry.MaxPlayers.Should().Be(50);
        entry.LobbyWindowSeconds.Should().Be(90);
        entry.TickIntervalSeconds.Should().Be(5);
        entry.RequiresEntryFee.Should().BeTrue();
    }
}
