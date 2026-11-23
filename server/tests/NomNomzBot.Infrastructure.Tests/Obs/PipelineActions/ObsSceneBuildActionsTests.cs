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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Infrastructure.Obs;
using NomNomzBot.Infrastructure.Obs.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs.PipelineActions;

/// <summary>
/// Proves the two scene-building actions send the exact OBS requests through the real control service, and that a
/// missing OBS connection ends as a failed step with the transport's code (the inbox folds those into one notice).
/// Only the OBS transport is faked.
/// </summary>
public sealed class ObsSceneBuildActionsTests
{
    private static readonly Guid Channel = Guid.Parse("019f2b00-4444-7000-8000-000000000001");

    private static (ObsControlService Control, List<ObsRequest> Sent) Build(
        bool connected = true,
        string existingInputs = "[]"
    )
    {
        List<ObsRequest> sent = [];
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
                sent.Add(request);
                if (!connected)
                    return Task.FromResult(
                        Result.Failure<ObsResponse>("OBS is not connected.", "OBS_NOT_CONNECTED")
                    );
                Dictionary<string, object?> data =
                    request.RequestType == "GetInputList"
                        ? new() { ["inputs"] = existingInputs }
                        : [];
                return Task.FromResult(Result.Success(new ObsResponse(true, data, null)));
            });
        return (new ObsControlService(transport), sent);
    }

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "TestUser",
            MessageId = "m1",
            RawMessage = "",
        };

    private static ActionDefinition Definition(string type, string json) =>
        new()
        {
            Type = type,
            Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json),
        };

    [Fact]
    public async Task Create_scene_sends_CreateScene_with_the_name()
    {
        (ObsControlService control, List<ObsRequest> sent) = Build();

        ActionResult result = await new ObsCreateSceneAction(control).ExecuteAsync(
            Context(),
            Definition("obs_create_scene", """{"scene_name":"Starting soon"}""")
        );

        result.Succeeded.Should().BeTrue();
        sent.Select(r => r.RequestType).Should().Equal("CreateScene");
        sent[0]
            .RequestData.Should()
            .BeEquivalentTo(new Dictionary<string, object?> { ["sceneName"] = "Starting soon" });
    }

    [Fact]
    public async Task Create_scene_without_a_name_fails_and_sends_nothing()
    {
        (ObsControlService control, List<ObsRequest> sent) = Build();

        ActionResult result = await new ObsCreateSceneAction(control).ExecuteAsync(
            Context(),
            Definition("obs_create_scene", "{}")
        );

        result.Succeeded.Should().BeFalse();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_browser_source_creates_the_input_with_the_given_settings()
    {
        (ObsControlService control, List<ObsRequest> sent) = Build();

        ActionResult result = await new ObsAddBrowserSourceAction(control).ExecuteAsync(
            Context(),
            Definition(
                "obs_add_browser_source",
                """
                {"scene":"Main","source_name":"Chat","url":"https://x.test/o","width":800,"height":600,"control_audio":true,"shutdown_when_hidden":true}
                """
            )
        );

        result.Succeeded.Should().BeTrue();
        sent.Select(r => r.RequestType).Should().Equal("GetInputList", "CreateInput");
        sent[1]
            .RequestData!["inputSettings"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["url"] = "https://x.test/o",
                    ["width"] = 800,
                    ["height"] = 600,
                    ["reroute_audio"] = true,
                    ["shutdown"] = true,
                }
            );
        sent[1].RequestData!["sceneName"].Should().Be("Main");
        sent[1].RequestData!["inputName"].Should().Be("Chat");
        sent[1].RequestData!["inputKind"].Should().Be("browser_source");
    }

    [Fact]
    public async Task Add_browser_source_updates_an_input_that_already_exists()
    {
        (ObsControlService control, List<ObsRequest> sent) = Build(
            existingInputs: """[{"inputName":"Chat","inputKind":"browser_source"}]"""
        );

        ActionResult result = await new ObsAddBrowserSourceAction(control).ExecuteAsync(
            Context(),
            Definition(
                "obs_add_browser_source",
                """{"scene":"Main","source_name":"Chat","url":"https://x.test/o"}"""
            )
        );

        result.Succeeded.Should().BeTrue();
        sent.Select(r => r.RequestType).Should().Equal("GetInputList", "SetInputSettings");
        sent[1]
            .RequestData!["inputSettings"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, object?>
                {
                    ["url"] = "https://x.test/o",
                    ["width"] = 1920,
                    ["height"] = 1080,
                    ["reroute_audio"] = false,
                    ["shutdown"] = false,
                }
            );
    }

    [Fact]
    public async Task No_OBS_connection_fails_the_step_with_the_transport_code_and_records_the_error()
    {
        (ObsControlService control, List<ObsRequest> _) = Build(connected: false);
        PipelineExecutionContext ctx = Context();

        ActionResult result = await new ObsAddBrowserSourceAction(control).ExecuteAsync(
            ctx,
            Definition(
                "obs_add_browser_source",
                """{"scene":"Main","source_name":"Chat","url":"https://x.test/o"}"""
            )
        );

        result.Succeeded.Should().BeFalse();
        result.FailureShape.Should().BeNull();
        ctx.Variables["obs.last_error"].Should().Be("OBS is not connected.");
    }
}
