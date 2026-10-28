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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Obs.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs.PipelineActions;

/// <summary>
/// S-STREAMDECK-OBS-REMAINDER: proves <c>obs_request</c>/<c>obs_call_vendor</c>'s <c>request_data</c>
/// and <c>obs_request_batch</c>'s <c>requests</c> actually reach <see cref="IObsControlService"/> as
/// real JSON objects/arrays end-to-end through the REAL <see cref="PipelineEngine"/> — not a unit test
/// of the engine's resolution helper in isolation. Before the fix, <c>PipelineEngine.ResolveTemplatedFieldsAsync</c>
/// always flattened a resolved Templated Text field to a JSON string, so
/// <see cref="ObsRequestAction.ReadDataObject"/> (which requires <c>ValueKind == JsonValueKind.Object</c>)
/// silently returned null, and <c>obs_request_batch</c> (which requires <c>ValueKind == JsonValueKind.Array</c>)
/// failed outright with "needs a 'requests' array".
/// </summary>
public sealed class ObsOutputAndRawActionsTests
{
    private static readonly Guid Channel = Guid.Parse("019f2b00-3333-7000-8000-000000000001");

    /// <summary>Identity resolver — every template used in this file's configured values has no
    /// <c>{{ }}</c> placeholders, so the resolver's contract (hand plain text straight back) is what
    /// exercises the "resolved text happens to be valid JSON" path.</summary>
    private sealed class IdentityResolver : ITemplateResolver
    {
        public string Resolve(string template, IDictionary<string, string> variables) => template;

        public Task<string> ResolveAsync(
            string template,
            IDictionary<string, string> seedVariables,
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(template);
    }

    private static PipelineEngine CreateEngine(IObsControlService obs)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();

        return new(
            db,
            registry,
            [
                new ObsRequestAction(obs),
                new ObsRequestBatchAction(obs),
                new ObsCallVendorAction(obs),
            ],
            [],
            new IdentityResolver(),
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );
    }

    private static PipelineRequest Request(string json) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "TestUser",
            PipelineJson = json,
            MessageId = "m1",
            RawMessage = "",
        };

    [Fact]
    public async Task ObsRequest_RequestDataConfiguredAsJsonObjectText_ReachesObsControlServiceAsARealObject()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        ObsRequest? captured = null;
        obs.RequestAsync(
                Arg.Any<Guid>(),
                Arg.Do<ObsRequest>(r => captured = r),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ObsResponse(true, null, null)));

        PipelineEngine engine = CreateEngine(obs);
        const string requestData = /*lang=json*/
            """{"sceneName":"Main","sceneIndex":2}""";
        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"obs_request","request_type":"SetCurrentProgramScene","request_data":__REQUEST_DATA__}}]}""".Replace(
                "__REQUEST_DATA__",
                System.Text.Json.JsonSerializer.Serialize(requestData)
            );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        captured.Should().NotBeNull();
        captured!.RequestType.Should().Be("SetCurrentProgramScene");
        captured
            .RequestData.Should()
            .NotBeNull(
                "before the fix this was always null — the JSON object text was flattened to a JSON string that ReadDataObject rejects"
            );
        captured.RequestData!["sceneName"].Should().Be("Main");
        captured.RequestData!["sceneIndex"].Should().Be(2.0);
    }

    [Fact]
    public async Task ObsRequest_WhenObsIsNotConnected_FailsWithTheTransportCodeAndKeepsTheMessageInLastError()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.RequestAsync(Arg.Any<Guid>(), Arg.Any<ObsRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<ObsResponse>(
                    "No OBS bridge is connected for this channel.",
                    "OBS_BRIDGE_OFFLINE"
                )
            );
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "TestUser",
            MessageId = "m1",
            RawMessage = "",
        };
        ActionDefinition definition = new()
        {
            Type = "obs_request",
            Parameters = new()
            {
                ["request_type"] = System.Text.Json.JsonSerializer.SerializeToElement("GetVersion"),
            },
        };

        ActionResult result = await new ObsRequestAction(obs).ExecuteAsync(ctx, definition);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("OBS_BRIDGE_OFFLINE");
        result.ErrorMessage.Should().Be("No OBS bridge is connected for this channel.");
        ctx.Variables["obs.last_error"].Should().Be("No OBS bridge is connected for this channel.");
    }

    [Fact]
    public async Task ObsCallVendor_RequestDataConfiguredAsJsonObjectText_ReachesObsControlServiceAsARealObject()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        IReadOnlyDictionary<string, object?>? captured = null;
        obs.CallVendorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, object?>?>(d => captured = d),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ObsResponse(true, null, null)));

        PipelineEngine engine = CreateEngine(obs);
        const string requestData = /*lang=json*/
            """{"opacity":0.5}""";
        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"obs_call_vendor","vendor":"obs-websocket","request_type":"SetOpacity","request_data":__REQUEST_DATA__}}]}""".Replace(
                "__REQUEST_DATA__",
                System.Text.Json.JsonSerializer.Serialize(requestData)
            );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        captured.Should().NotBeNull();
        captured!["opacity"].Should().Be(0.5);
    }

    [Fact]
    public async Task ObsRequestBatch_RequestsConfiguredAsJsonArrayText_ReachesObsControlServiceAsARealArray()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        ObsRequestBatch? captured = null;
        obs.RequestBatchAsync(
                Arg.Any<Guid>(),
                Arg.Do<ObsRequestBatch>(b => captured = b),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<ObsResponse>>([
                    new(true, null, null),
                    new(true, null, null),
                ])
            );

        PipelineEngine engine = CreateEngine(obs);
        const string requests = /*lang=json*/
            """[{"request_type":"StartRecord"},{"request_type":"StartStream"}]""";
        string json = /*lang=json*/
        """{"steps":[{"action":{"type":"obs_request_batch","requests":__REQUESTS__}}]}""".Replace(
            "__REQUESTS__",
            System.Text.Json.JsonSerializer.Serialize(requests)
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        // Before the fix: "requests" stayed a JSON string, so ObsRequestBatchAction's own
        // `requestsEl.ValueKind != JsonValueKind.Array` guard failed the step outright with
        // "obs_request_batch needs a 'requests' array" — this proves it now runs to completion with
        // the real two-item batch forwarded to the service.
        result.Outcome.Should().Be(PipelineOutcome.Completed);
        captured.Should().NotBeNull();
        captured!.Requests.Should().HaveCount(2);
        captured.Requests[0].RequestType.Should().Be("StartRecord");
        captured.Requests[1].RequestType.Should().Be("StartStream");
    }
}
