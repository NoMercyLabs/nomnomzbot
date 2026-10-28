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
/// Proves the scene-collection and profile reads and switches forward to <see cref="IObsControlService"/>,
/// degrade to an empty 200 when OBS is unreachable, and carry the chosen name into the service call.
/// </summary>
public sealed class ObsControllerCollectionsAndProfilesTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f7d3");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetSceneCollections_returns_the_services_collection_list()
    {
        IReadOnlyList<ObsSceneCollectionDto> collections =
        [
            new("Chatting", false),
            new("Gaming", true),
        ];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneCollectionsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(collections));

        IActionResult result = await Build(control).GetSceneCollections(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsSceneCollectionDto>>)ok.Value!)
            .Data.Should()
            .BeEquivalentTo(collections);
    }

    [Fact]
    public async Task GetSceneCollections_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetSceneCollectionsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ObsSceneCollectionDto>>("off", "OBS_DISABLED"));

        IActionResult result = await Build(control).GetSceneCollections(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsSceneCollectionDto>>)ok.Value!)
            .Data.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task SetCurrentSceneCollection_passes_the_name_to_the_service()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetSceneCollectionAsync(Channel, "Gaming", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetCurrentSceneCollection(Channel, new("Gaming"), default);

        await control
            .Received(1)
            .SetSceneCollectionAsync(Channel, "Gaming", Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetProfiles_returns_the_services_profile_list()
    {
        IReadOnlyList<ObsProfileDto> profiles = [new("Untitled", true), new("Low bitrate", false)];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetProfilesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(profiles));

        IActionResult result = await Build(control).GetProfiles(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsProfileDto>>)ok.Value!)
            .Data.Should()
            .BeEquivalentTo(profiles);
    }

    [Fact]
    public async Task GetProfiles_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetProfilesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ObsProfileDto>>("off", "OBS_DISABLED"));

        IActionResult result = await Build(control).GetProfiles(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<ObsProfileDto>>)ok.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SetCurrentProfile_passes_the_name_to_the_service()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .SetProfileAsync(Channel, "Low bitrate", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .SetCurrentProfile(Channel, new("Low bitrate"), default);

        await control
            .Received(1)
            .SetProfileAsync(Channel, "Low bitrate", Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }
}
