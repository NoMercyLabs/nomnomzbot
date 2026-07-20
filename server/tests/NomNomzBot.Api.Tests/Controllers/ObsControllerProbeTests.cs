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
/// Proves the OBS connectivity probe is TRUTHFUL — the opposite of the passive state/scenes/inputs reads.
/// Those mask a "not connected yet" as an empty 200 so the page shows its connect prompt (not a 500), which is
/// exactly why a 200 there cannot be trusted as "reachable". The probe actively runs a harmless request and
/// reports the real outcome so the dashboard can tell connected from the masked empty state: connected when the
/// transport answers, and the genuine failing code (never swallowed) when it does not.
/// </summary>
public sealed class ObsControllerProbeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7c4");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    private static ObsProbeDto Probe(IActionResult result)
    {
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsProbeDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsProbeDto>>()
            .Subject;
        body.Data.Should().NotBeNull();
        return body.Data!;
    }

    [Fact]
    public async Task Probe_reports_connected_when_the_transport_answers()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .RequestAsync(Channel, Arg.Any<ObsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ObsResponse(true, null, null)));

        ObsProbeDto probe = Probe(await Build(control).Probe(Channel, default));

        probe.Connected.Should().BeTrue();
        probe.ErrorCode.Should().BeNull();
        probe.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("OBS_NOT_CONNECTED")]
    [InlineData("OBS_DISABLED")]
    [InlineData("OBS_BRIDGE_OFFLINE")]
    [InlineData("OBS_WRONG_MODE")]
    public async Task Probe_reports_the_real_failing_code_without_masking(string code)
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .RequestAsync(Channel, Arg.Any<ObsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ObsResponse>("Could not reach OBS.", code));

        ObsProbeDto probe = Probe(await Build(control).Probe(Channel, default));

        probe.Connected.Should().BeFalse();
        probe.ErrorCode.Should().Be(code);
        probe.Error.Should().Be("Could not reach OBS.");
    }

    [Fact]
    public async Task Probe_runs_a_harmless_read_only_GetVersion_request()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .RequestAsync(Channel, Arg.Any<ObsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ObsResponse(true, null, null)));

        await Build(control).Probe(Channel, default);

        await control
            .Received(1)
            .RequestAsync(
                Channel,
                Arg.Is<ObsRequest>(r => r.RequestType == "GetVersion"),
                Arg.Any<CancellationToken>()
            );
    }
}
