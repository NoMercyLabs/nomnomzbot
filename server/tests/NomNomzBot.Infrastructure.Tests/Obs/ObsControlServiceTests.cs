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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Infrastructure.Obs;
using NomNomzBot.Infrastructure.Obs.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the D7 request-building nuances (obs-control.md §3.1/§5): source visibility resolves the
/// numeric scene-item id FIRST and then enables with exactly that id; volume enforces dB XOR
/// multiplier; media verbs map to the OBS wire constants; and the pipeline actions parse their
/// config (variables included) into the same typed calls — a failed action writes
/// <c>obs.last_error</c> and fails closed.
/// </summary>
public sealed class ObsControlServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f801");

    private sealed class Harness
    {
        public required ObsControlService Service { get; init; }
        public required IObsTransport Transport { get; init; }
        public required List<ObsRequest> Requests { get; init; }
    }

    private static Harness Build(Func<ObsRequest, ObsResponse>? responder = null)
    {
        List<ObsRequest> requests = [];
        IObsTransport transport = Substitute.For<IObsTransport>();
        transport
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<ObsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                ObsRequest request = ci.ArgAt<ObsRequest>(2);
                lock (requests)
                    requests.Add(request);
                ObsResponse response =
                    responder?.Invoke(request) ?? new ObsResponse(true, null, null);
                return Task.FromResult(Result.Success(response));
            });
        return new Harness
        {
            Service = new ObsControlService(transport),
            Transport = transport,
            Requests = requests,
        };
    }

    [Fact]
    public async Task Source_visibility_resolves_the_item_id_first_then_enables_with_it()
    {
        Harness h = Build(request =>
            request.RequestType == "GetSceneItemId"
                ? new ObsResponse(
                    true,
                    new Dictionary<string, object?> { ["sceneItemId"] = 42d },
                    null
                )
                : new ObsResponse(true, null, null)
        );

        Result result = await h.Service.SetSourceVisibleAsync(
            Channel,
            "Live",
            "Cam",
            visible: false
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        h.Requests.Select(r => r.RequestType)
            .Should()
            .ContainInOrder("GetSceneItemId", "SetSceneItemEnabled");
        ObsRequest enable = h.Requests.Single(r => r.RequestType == "SetSceneItemEnabled");
        enable.RequestData!["sceneItemId"].Should().Be(42);
        enable.RequestData!["sceneItemEnabled"].Should().Be(false);
    }

    [Fact]
    public async Task Volume_takes_db_xor_multiplier()
    {
        Harness h = Build();

        Result both = await h.Service.SetInputVolumeAsync(Channel, "Mic", -6.0, 0.5);
        both.IsFailure.Should().BeTrue("dB and multiplier together is ambiguous");
        both.ErrorCode.Should().Be("VALIDATION_FAILED");

        Result neither = await h.Service.SetInputVolumeAsync(Channel, "Mic", null, null);
        neither.IsFailure.Should().BeTrue();

        Result db = await h.Service.SetInputVolumeAsync(Channel, "Mic", -6.0, null);
        db.IsSuccess.Should().BeTrue(db.ErrorMessage);
        ObsRequest sent = h.Requests.Single();
        sent.RequestData!.Should().ContainKey("inputVolumeDb");
        sent.RequestData!.Should().NotContainKey("inputVolumeMul");
    }

    [Fact]
    public async Task Media_verbs_map_to_the_obs_wire_constants()
    {
        Harness h = Build();

        await h.Service.TriggerMediaAsync(Channel, "Video", MediaAction.Restart);

        h.Requests.Single()
            .RequestData!["mediaAction"]
            .Should()
            .Be("OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART");
    }

    [Fact]
    public async Task The_set_source_action_parses_config_and_fails_closed()
    {
        Harness h = Build(request =>
            request.RequestType == "GetSceneItemId"
                ? new ObsResponse(
                    true,
                    new Dictionary<string, object?> { ["sceneItemId"] = 7d },
                    null
                )
                : new ObsResponse(true, null, null)
        );
        ObsSetSourceAction action = new(h.Service);
        PipelineExecutionContext ctx = NewContext();
        ctx.Variables["target_scene"] = "Live";

        // Variable-resolved scene + explicit source + visible=false.
        ActionResult ok = await action.ExecuteAsync(
            ctx,
            Definition(
                """{ "type": "obs_set_source", "scene": "{target_scene}", "source": "Cam", "visible": false }"""
            )
        );
        ok.Succeeded.Should().BeTrue(ok.ErrorMessage);
        h.Requests.Single(r => r.RequestType == "GetSceneItemId")
            .RequestData!["sceneName"]
            .Should()
            .Be("Live");

        // Missing config fails without touching OBS.
        int requestsBefore = h.Requests.Count;
        ActionResult missing = await action.ExecuteAsync(
            NewContext(),
            Definition("""{ "type": "obs_set_source", "source": "Cam" }""")
        );
        missing.Succeeded.Should().BeFalse();
        h.Requests.Count.Should().Be(requestsBefore, "a rejected action never reaches OBS");
    }

    [Fact]
    public async Task A_failed_obs_call_lands_in_obs_last_error()
    {
        Harness h = Build(_ => new ObsResponse(false, null, "output already active"));
        ObsRecordingAction action = new(h.Service);
        PipelineExecutionContext ctx = NewContext();

        ActionResult result = await action.ExecuteAsync(
            ctx,
            Definition("""{ "type": "obs_recording", "action": "start" }""")
        );

        result.Succeeded.Should().BeFalse();
        ctx.Variables["obs.last_error"].Should().Be("output already active");
        h.Requests.Single().RequestType.Should().Be("StartRecord");
    }

    private static PipelineExecutionContext NewContext() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u",
            TriggeredByDisplayName = "U",
            MessageId = "",
            RawMessage = "",
        };

    private static ActionDefinition Definition(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(json)!;
}
