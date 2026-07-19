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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the OBS state reads degrade gracefully: when OBS simply is not reachable (not enabled, no socket,
/// no bridge leader) a read of state/scenes/inputs is the NORMAL "not connected yet" case, so it returns an
/// empty/disconnected payload at 200 — never the scary "Internal Server Error" (HTTP 500) the raw
/// transport failure used to bubble up onto the dashboard. A genuine failure still surfaces as an error.
/// </summary>
public sealed class ObsControllerReadStateTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7b3");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Theory]
    [InlineData("OBS_DISABLED")]
    [InlineData("OBS_NOT_CONNECTED")]
    [InlineData("OBS_BRIDGE_OFFLINE")]
    [InlineData("OBS_WRONG_MODE")]
    public async Task GetState_returns_a_disconnected_state_at_200_when_obs_is_unavailable(
        string unavailableCode
    )
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStateAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsStateDto>("OBS control is not enabled.", unavailableCode));

        IActionResult result = await Build(control).GetState(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsStateDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsStateDto>>()
            .Subject;
        body.Data.Should().NotBeNull();
        body.Data!.CurrentScene.Should().BeNull();
        body.Data.Streaming.Should().BeFalse();
        body.Data.Recording.Should().BeFalse();
    }

    [Fact]
    public async Task GetScenes_and_GetInputs_return_empty_lists_at_200_when_disconnected()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetScenesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ObsSceneDto>>("not enabled", "OBS_DISABLED"));
        control
            .GetInputsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ObsInputDto>>("not enabled", "OBS_DISABLED"));

        ObsController sut = Build(control);

        OkObjectResult scenes = (await sut.GetScenes(Channel, default))
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject;
        ((StatusResponseDto<IReadOnlyList<ObsSceneDto>>)scenes.Value!).Data.Should().BeEmpty();

        OkObjectResult inputs = (await sut.GetInputs(Channel, default))
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject;
        ((StatusResponseDto<IReadOnlyList<ObsInputDto>>)inputs.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task A_genuine_failure_is_not_masked_as_an_empty_ok()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStateAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsStateDto>("Bad request.", "VALIDATION_FAILED"));

        IActionResult result = await Build(control).GetState(Channel, default);

        // A real error must not be swallowed into a 200 empty state — it maps to its proper non-2xx response.
        result.Should().NotBeOfType<OkObjectResult>();
    }
}
