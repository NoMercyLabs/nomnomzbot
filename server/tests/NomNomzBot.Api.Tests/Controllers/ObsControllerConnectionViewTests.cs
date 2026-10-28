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

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The OBS connection read has two views. <c>status</c> is the connection state alone and is open to anyone
/// holding <c>obs:scene:read</c> (the moderator panels need it); <c>full</c> is the configuration and still needs
/// <c>obs:config:read</c>. The status shape must carry no configuration field at all.
/// </summary>
public sealed class ObsControllerConnectionViewTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7b4");

    private static readonly ObsConnectionDto Stored = new(
        "direct",
        "192.168.1.50",
        4455,
        HasPassword: true,
        HasBridgeToken: true,
        EventSubscriptionsMask: 7,
        IsEnabled: true,
        LastConnectedAt: new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc),
        LastError: "connect 192.168.1.50:4455 refused",
        EventKinds: []
    );

    private static ObsController Build(IObsConnectionService connections)
    {
        connections.GetAsync(Channel, Arg.Any<CancellationToken>()).Returns(Result.Success(Stored));
        return new ObsController(
            connections,
            Substitute.For<IObsControlService>(),
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() },
            },
        };
    }

    private static IAuthorizationService Authorization(bool configRead)
    {
        IAuthorizationService authorization = Substitute.For<IAuthorizationService>();
        authorization
            .AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<object?>(),
                ActionAuthorizationPolicy.For("obs:config:read")
            )
            .Returns(configRead ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return authorization;
    }

    [Fact]
    public async Task Status_view_returns_the_state_without_any_configuration_field()
    {
        ObsController sut = Build(Substitute.For<IObsConnectionService>());

        IActionResult result = await sut.GetConnection(
            Channel,
            Authorization(configRead: false),
            default,
            ObsConnectionView.Status
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsConnectionStatusDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsConnectionStatusDto>>()
            .Subject;
        body.Data!.Mode.Should().Be("direct");
        body.Data.IsEnabled.Should().BeTrue();
        body.Data.LastConnectedAt.Should().Be(Stored.LastConnectedAt);

        string json = JsonSerializer.Serialize(body.Data);
        foreach (
            string secret in new[]
            {
                "192.168.1.50",
                "4455",
                "Host",
                "Port",
                "Password",
                "BridgeToken",
                "LastError",
                "Mask",
            }
        )
            json.Should().NotContain(secret);
    }

    [Fact]
    public void Status_dto_has_only_mode_enabled_and_last_connected()
    {
        typeof(ObsConnectionStatusDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .Should()
            .BeEquivalentTo("Mode", "IsEnabled", "LastConnectedAt");
    }

    [Fact]
    public async Task Full_view_returns_the_configuration_to_a_caller_with_config_read()
    {
        ObsController sut = Build(Substitute.For<IObsConnectionService>());

        IActionResult result = await sut.GetConnection(
            Channel,
            Authorization(configRead: true),
            default
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsConnectionDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsConnectionDto>>()
            .Subject;
        body.Data.Should().Be(Stored);
    }

    [Fact]
    public async Task Full_view_is_refused_to_a_caller_without_config_read()
    {
        IObsConnectionService connections = Substitute.For<IObsConnectionService>();
        ObsController sut = Build(connections);

        IActionResult result = await sut.GetConnection(
            Channel,
            Authorization(configRead: false),
            default
        );

        result.Should().BeOfType<ForbidResult>();
        await connections.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Connection_read_is_gated_by_obs_scene_read_so_the_status_view_is_reachable()
    {
        MethodInfo read = typeof(ObsController).GetMethod(nameof(ObsController.GetConnection))!;

        RequireActionAttribute gate = read.GetCustomAttribute<RequireActionAttribute>()!;

        gate.ActionKey.Should().Be("obs:scene:read");
    }
}
