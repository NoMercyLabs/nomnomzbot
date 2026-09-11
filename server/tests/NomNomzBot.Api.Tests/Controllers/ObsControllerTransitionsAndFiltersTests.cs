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
/// S-OBS-PROTOCOL-GAPS (remainder): proves the scene-transition-list and source-filter-list reads
/// forward to <see cref="IObsControlService"/> and round-trip its real response shape, and degrade to
/// an empty 200 — never a 500 — when OBS is unreachable, exactly like the existing
/// state/scenes/inputs/scene-items/virtual-cam/stats reads.
/// </summary>
public sealed class ObsControllerTransitionsAndFiltersTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7d2");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetSceneTransitions_returns_the_services_transition_list()
    {
        IReadOnlyList<ObsTransitionDto> transitions = [new("Fade", true), new("Cut", false)];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneTransitionListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(transitions));

        IActionResult result = await Build(control).GetSceneTransitions(Channel, default);

        await control
            .Received(1)
            .GetSceneTransitionListAsync(Channel, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ObsTransitionDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<ObsTransitionDto>>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(transitions);
    }

    [Fact]
    public async Task GetSceneTransitions_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneTransitionListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsTransitionDto>>(
                    "OBS control is not enabled.",
                    "OBS_DISABLED"
                )
            );

        IActionResult result = await Build(control).GetSceneTransitions(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsTransitionDto>>)ok.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSourceFilters_forwards_the_source_name_and_returns_the_filters()
    {
        IReadOnlyList<ObsFilterDto> filters =
        [
            new("Chroma Key", "chroma_key_filter_v2", true, 0),
            new("Color Correction", "color_filter_v2", false, 1),
        ];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>())
            .Returns(Result.Success(filters));

        IActionResult result = await Build(control).GetSourceFilters(Channel, "Webcam", default);

        await control
            .Received(1)
            .GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ObsFilterDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<ObsFilterDto>>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(filters);
    }

    [Fact]
    public async Task GetSourceFilters_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsFilterDto>>("not connected", "OBS_NOT_CONNECTED")
            );

        IActionResult result = await Build(control).GetSourceFilters(Channel, "Webcam", default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsFilterDto>>)ok.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task A_genuine_failure_is_not_masked_as_an_empty_ok()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsFilterDto>>("Bad request.", "VALIDATION_FAILED")
            );

        IActionResult result = await Build(control).GetSourceFilters(Channel, "Webcam", default);

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
