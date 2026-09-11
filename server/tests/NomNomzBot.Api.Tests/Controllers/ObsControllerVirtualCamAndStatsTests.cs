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
/// S-OBS-PROTOCOL-GAPS (partial): proves the virtual-cam-status and stats reads forward to
/// <see cref="IObsControlService"/> and round-trip its real response shape, and degrade to an
/// empty/zeroed 200 — never a 500 — when OBS is unreachable, exactly like the existing
/// state/scenes/inputs/scene-items reads.
/// </summary>
public sealed class ObsControllerVirtualCamAndStatsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7c1");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetVirtualCamStatus_returns_the_services_real_output_active_flag()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetVirtualCamStatusAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ObsVirtualCamStatusDto(true)));

        IActionResult result = await Build(control).GetVirtualCamStatus(Channel, default);

        await control.Received(1).GetVirtualCamStatusAsync(Channel, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsVirtualCamStatusDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsVirtualCamStatusDto>>()
            .Subject;
        body.Data!.OutputActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetVirtualCamStatus_returns_an_inactive_default_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetVirtualCamStatusAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<ObsVirtualCamStatusDto>(
                    "OBS control is not enabled.",
                    "OBS_DISABLED"
                )
            );

        IActionResult result = await Build(control).GetVirtualCamStatus(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<ObsVirtualCamStatusDto>)ok.Value!)
            .Data!.OutputActive.Should()
            .BeFalse();
    }

    [Fact]
    public async Task GetStats_returns_the_services_real_performance_counters()
    {
        ObsStatsDto stats = new(12.5, 512.25, 59.94, 10000, 3, 9998, 1);
        IObsControlService control = Substitute.For<IObsControlService>();
        control.GetStatsAsync(Channel, Arg.Any<CancellationToken>()).Returns(Result.Success(stats));

        IActionResult result = await Build(control).GetStats(Channel, default);

        await control.Received(1).GetStatsAsync(Channel, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsStatsDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsStatsDto>>()
            .Subject;
        body.Data.Should().Be(stats);
    }

    [Fact]
    public async Task GetStats_returns_zeroed_counters_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStatsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsStatsDto>("not connected", "OBS_NOT_CONNECTED"));

        IActionResult result = await Build(control).GetStats(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<ObsStatsDto>)ok.Value!)
            .Data.Should()
            .Be(new ObsStatsDto(0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task A_genuine_failure_is_not_masked_as_an_empty_ok()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetStatsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsStatsDto>("Bad request.", "VALIDATION_FAILED"));

        IActionResult result = await Build(control).GetStats(Channel, default);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
