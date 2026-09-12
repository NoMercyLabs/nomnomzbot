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
/// S-OBS-UI-REMAINDER (deferred trio): proves the hotkey enumeration/trigger, source-screenshot, and
/// raw batch/vendor pass-through routes forward their arguments to <see cref="IObsControlService"/>
/// unchanged and round-trip its real response shape — the same contract every other OBS route in this
/// controller already proves (see <see cref="ObsControllerTransitionsAndFiltersTests"/>).
/// </summary>
public sealed class ObsControllerHotkeysScreenshotAndPassthroughTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f9a1");

    private static ObsController Build(IObsControlService control) =>
        new(
            Substitute.For<IObsConnectionService>(),
            control,
            Substitute.For<IObsBridgeRegistry>(),
            Substitute.For<IConfiguration>()
        );

    [Fact]
    public async Task GetHotkeys_returns_the_services_hotkey_list()
    {
        IReadOnlyList<string> hotkeys = ["OBSBasic.StartStreaming", "ReplayBuffer.Save"];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetHotkeyListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(hotkeys));

        IActionResult result = await Build(control).GetHotkeys(Channel, default);

        await control.Received(1).GetHotkeyListAsync(Channel, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<string>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<string>>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(hotkeys);
    }

    [Fact]
    public async Task GetHotkeys_returns_an_empty_list_at_200_when_obs_is_unavailable()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .GetHotkeyListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<string>>("not connected", "OBS_NOT_CONNECTED"));

        IActionResult result = await Build(control).GetHotkeys(Channel, default);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<IReadOnlyList<string>>)ok.Value!).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task TriggerHotkey_forwards_the_hotkey_name()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .TriggerHotkeyAsync(Channel, "OBSBasic.StartStreaming", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        IActionResult result = await Build(control)
            .TriggerHotkey(
                Channel,
                new ObsHotkeyTriggerRequest("OBSBasic.StartStreaming"),
                default
            );

        await control
            .Received(1)
            .TriggerHotkeyAsync(Channel, "OBSBasic.StartStreaming", Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task CaptureScreenshot_forwards_source_and_format_and_returns_the_image_data_uri()
    {
        const string ImageDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==";
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .ScreenshotAsync(Channel, "Webcam", "png", Arg.Any<CancellationToken>())
            .Returns(Result.Success(ImageDataUri));

        IActionResult result = await Build(control)
            .CaptureScreenshot(Channel, new ObsScreenshotRequest("Webcam", "png"), default);

        await control
            .Received(1)
            .ScreenshotAsync(Channel, "Webcam", "png", Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<string> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<string>>()
            .Subject;
        body.Data.Should().Be(ImageDataUri);
    }

    [Fact]
    public async Task CaptureScreenshot_surfaces_a_failure_when_the_source_is_missing()
    {
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .ScreenshotAsync(Channel, "Missing", "png", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>("source not found", "NOT_FOUND"));

        IActionResult result = await Build(control)
            .CaptureScreenshot(Channel, new ObsScreenshotRequest("Missing", "png"), default);

        result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RawRequestBatch_forwards_the_batch_and_returns_one_response_per_request()
    {
        ObsRequestBatch batch = new(
            [new("GetVersion", null), new("GetSceneList", null)],
            ObsBatchExecution.SerialRealtime
        );
        IReadOnlyList<ObsResponse> responses =
        [
            new(true, new Dictionary<string, object?> { ["obsVersion"] = "31.0.0" }, null),
            new(true, null, null),
        ];
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .RequestBatchAsync(Channel, batch, Arg.Any<CancellationToken>())
            .Returns(Result.Success(responses));

        IActionResult result = await Build(control).RawRequestBatch(Channel, batch, default);

        await control.Received(1).RequestBatchAsync(Channel, batch, Arg.Any<CancellationToken>());
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<IReadOnlyList<ObsResponse>> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<IReadOnlyList<ObsResponse>>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(responses);
    }

    [Fact]
    public async Task CallVendor_forwards_vendor_name_request_type_and_data()
    {
        Dictionary<string, object?> data = new() { ["sourceName"] = "Webcam" };
        ObsResponse response = new(true, new Dictionary<string, object?> { ["ok"] = true }, null);
        IObsControlService control = Substitute.For<IObsControlService>();
        control
            .CallVendorAsync(
                Channel,
                "obs-websocket",
                "GetVersion",
                data,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(response));

        IActionResult result = await Build(control)
            .CallVendor(
                Channel,
                new ObsVendorRequest("obs-websocket", "GetVersion", data),
                default
            );

        await control
            .Received(1)
            .CallVendorAsync(
                Channel,
                "obs-websocket",
                "GetVersion",
                data,
                Arg.Any<CancellationToken>()
            );
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<ObsResponse> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ObsResponse>>()
            .Subject;
        body.Data.Should().BeEquivalentTo(response);
    }
}
