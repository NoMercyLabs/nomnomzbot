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
/// S-OBS-PROTOCOL-GAPS (closing): proves the studio-mode read forwards to
/// <see cref="IObsControlService"/> and degrades to a disabled-default 200 — never a 500 — when OBS is
/// unreachable, exactly like the other reads, and that the studio-mode write forwards the requested
/// flag and surfaces a service failure as a non-2xx response instead of swallowing it.
/// </summary>
public sealed class ObsControllerStudioModeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7e3");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetStudioMode_returns_the_services_real_enabled_flag()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStudioModeEnabledAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ObsStudioModeStatusDto(true)));

        IActionResult result = await Build(control).GetStudioMode(Channel, default);

        await control.Received(1).GetStudioModeEnabledAsync(Channel, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsStudioModeStatusDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsStudioModeStatusDto>>()
            .Subject;
        body.Data!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetStudioMode_returns_a_disabled_default_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStudioModeEnabledAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<ObsStudioModeStatusDto>(
                    "OBS control is not enabled.",
                    "OBS_DISABLED"
                )
            );

        IActionResult result = await Build(control).GetStudioMode(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<ObsStudioModeStatusDto>)ok.Value!).Data!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetStudioMode_genuine_failure_is_not_masked_as_an_empty_ok()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStudioModeEnabledAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsStudioModeStatusDto>("Bad request.", "VALIDATION_FAILED"));

        IActionResult result = await Build(control).GetStudioMode(Channel, default);

        result.Should().NotBeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetStudioMode_forwards_the_requested_flag_to_the_service(bool enabled)
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetStudioModeEnabledAsync(Channel, enabled, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetStudioMode(Channel, new ObsStudioModeRequest(enabled), default);

        await control
            .Received(1)
            .SetStudioModeEnabledAsync(Channel, enabled, Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetStudioMode_surfaces_a_service_failure_as_a_non_2xx_response()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetStudioModeEnabledAsync(Channel, true, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("OBS rejected the request.", "OBS_ERROR"));

        IActionResult result = await Build(control)
            .SetStudioMode(Channel, new ObsStudioModeRequest(true), default);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
