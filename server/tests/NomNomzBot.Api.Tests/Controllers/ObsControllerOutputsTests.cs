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
/// Proves the S-PL5b output-control routes (replay buffer start/stop/save, virtual cam) forward the exact
/// request to the matching <see cref="IObsControlService"/> method — these are the server-ready capabilities
/// (obs-control.md; <see cref="ObsControlService"/>-equivalent typed wrappers) that had no controller endpoint
/// before this slice — and that a service failure surfaces as a non-2xx response rather than being swallowed.
/// </summary>
public sealed class ObsControllerOutputsTests
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
    [InlineData(ObsToggle.Start)]
    [InlineData(ObsToggle.Stop)]
    [InlineData(ObsToggle.Toggle)]
    public async Task SetReplayBuffer_forwards_the_requested_action_to_the_service(ObsToggle action)
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetReplayBufferAsync(Channel, action, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetReplayBuffer(Channel, new ObsToggleRequest(action), default);

        await control
            .Received(1)
            .SetReplayBufferAsync(Channel, action, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<StatusResponseDto<object?>>();
    }

    [Fact]
    public async Task SetReplayBuffer_surfaces_a_service_failure_as_a_non_2xx_response()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetReplayBufferAsync(Channel, ObsToggle.Start, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("OBS rejected the request.", "OBS_ERROR"));

        IActionResult result = await Build(control)
            .SetReplayBuffer(Channel, new ObsToggleRequest(ObsToggle.Start), default);

        result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SaveReplayBuffer_calls_the_service_with_no_body()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SaveReplayBufferAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control).SaveReplayBuffer(Channel, default);

        await control.Received(1).SaveReplayBufferAsync(Channel, Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData(ObsToggle.Start)]
    [InlineData(ObsToggle.Stop)]
    [InlineData(ObsToggle.Toggle)]
    public async Task SetVirtualCam_forwards_the_requested_action_to_the_service(ObsToggle action)
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetVirtualCamAsync(Channel, action, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetVirtualCam(Channel, new ObsToggleRequest(action), default);

        await control.Received(1).SetVirtualCamAsync(Channel, action, Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetVirtualCam_surfaces_a_service_failure_as_a_non_2xx_response()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetVirtualCamAsync(Channel, ObsToggle.Toggle, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("OBS not connected.", "OBS_NOT_CONNECTED"));

        IActionResult result = await Build(control)
            .SetVirtualCam(Channel, new ObsToggleRequest(ObsToggle.Toggle), default);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
