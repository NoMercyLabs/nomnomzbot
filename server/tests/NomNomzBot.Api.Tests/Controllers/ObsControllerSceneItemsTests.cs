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
/// S-OBS-SOURCE-VIS: proves the per-scene source-visibility surface — <c>GET scene-items</c> lists a
/// scene's items (with the per-item <c>sceneItemId</c>/<c>sourceName</c>/<c>sceneItemEnabled</c> the
/// dashboard needs to render toggles) and degrades to an empty 200 when OBS is unreachable, exactly like
/// the existing scenes/inputs reads; <c>POST scene-items/visibility</c> forwards to the SAME
/// <see cref="IObsControlService.SetSourceVisibleAsync"/> that already existed server-side but, until this
/// slice, had no controller endpoint to reach it from.
/// </summary>
public sealed class ObsControllerSceneItemsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7b3");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetSceneItems_forwards_the_scene_name_and_returns_the_items()
    {
        IReadOnlyList<ObsSceneItemDto> items = [new(1, "Webcam", true), new(2, "Overlay", false)];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneItemListAsync(Channel, "Live", Arg.Any<CancellationToken>())
            .Returns(Result.Success(items));

        IActionResult result = await Build(control).GetSceneItems(Channel, "Live", default);

        await control
            .Received(1)
            .GetSceneItemListAsync(Channel, "Live", Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ObsSceneItemDto>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<ObsSceneItemDto>>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task GetSceneItems_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneItemListAsync(Channel, "Live", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsSceneItemDto>>(
                    "OBS control is not enabled.",
                    "OBS_DISABLED"
                )
            );

        IActionResult result = await Build(control).GetSceneItems(Channel, "Live", default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsSceneItemDto>>)ok.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SetSourceVisibility_forwards_scene_source_and_visible_to_the_service()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetSourceVisibleAsync(Channel, "Live", "Webcam", false, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetSourceVisibility(
                Channel,
                new ObsSourceVisibilityRequest("Live", "Webcam", false),
                default
            );

        await control
            .Received(1)
            .SetSourceVisibleAsync(Channel, "Live", "Webcam", false, Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetSourceVisibility_surfaces_a_service_failure_as_a_non_2xx_response()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetSourceVisibleAsync(Channel, "Live", "Missing", true, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure("Source 'Missing' was not found in scene 'Live'.", "NOT_FOUND")
            );

        IActionResult result = await Build(control)
            .SetSourceVisibility(
                Channel,
                new ObsSourceVisibilityRequest("Live", "Missing", true),
                default
            );

        result.Should().NotBeOfType<OkObjectResult>();
    }
}
