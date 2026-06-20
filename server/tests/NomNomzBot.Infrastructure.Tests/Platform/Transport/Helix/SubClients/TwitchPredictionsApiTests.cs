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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// Behavioural tests for the Predictions sub-client: each method gates on the required scope, resolves the
/// tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query / body). The
/// capturing transport lets us assert the request shape and the short-circuit paths with no HTTP. Create and
/// End fold the resolved broadcaster id into the JSON body (not the query), so those tests assert the body.
/// </summary>
public class TwitchPredictionsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchPredictionsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchPrediction SamplePrediction(string status = "ACTIVE") =>
        new(
            "pred-1",
            TwitchId,
            "Name",
            "login",
            "Who wins?",
            status == "RESOLVED" ? "out-1" : null,
            [
                new TwitchPredictionOutcome(
                    "out-1",
                    "Blue team",
                    10,
                    5000,
                    [new TwitchPredictionTopPredictor("u1", "U1", "u1", 1000, 0)],
                    "BLUE"
                ),
                new TwitchPredictionOutcome("out-2", "Pink team", 3, 1200, null, "PINK"),
            ],
            120,
            status,
            DateTimeOffset.UnixEpoch,
            null,
            null
        );

    [Fact]
    public async Task GetPredictions_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPredictionsApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchPrediction>> result = await api.GetPredictionsAsync(
            Tenant,
            null,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPredictions_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPredictionsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadPredictions)
        );

        Result<TwitchPage<TwitchPrediction>> result = await api.GetPredictionsAsync(
            Tenant,
            null,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPredictions_WithScope_BuildsUserTokenPagedQuery_WithIdFilter_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchPrediction>([SamplePrediction()], "cursor", 0),
        };
        TwitchPredictionsApi api = Build(transport, TwitchScopes.ChannelReadPredictions);

        Result<TwitchPage<TwitchPrediction>> result = await api.GetPredictionsAsync(
            Tenant,
            ["pred-1", "pred-2"],
            new TwitchPageRequest(After: "abc", PageSize: 20)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        TwitchPrediction mapped = result.Value.Items.Should().ContainSingle().Subject;
        mapped.Status.Should().Be("ACTIVE");
        mapped.WinningOutcomeId.Should().BeNull();
        mapped.Outcomes.Should().HaveCount(2);
        mapped.Outcomes[0].Color.Should().Be("BLUE");
        mapped
            .Outcomes[0]
            .TopPredictors.Should()
            .ContainSingle()
            .Which.ChannelPointsUsed.Should()
            .Be(1000);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("predictions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "20");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "pred-1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "pred-2");
    }

    [Fact]
    public async Task CreatePrediction_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPredictionsApi api = Build(transport); // no scopes granted

        Result<TwitchPrediction> result = await api.CreatePredictionAsync(
            Tenant,
            new CreatePredictionRequest(
                "Who wins?",
                [new CreatePredictionOutcome("Blue"), new CreatePredictionOutcome("Pink")],
                120
            )
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreatePrediction_WithScope_BuildsUserTokenPost_WithBroadcasterInBody_MapsDto()
    {
        CapturingHelixTransport transport = new() { SingleResult = SamplePrediction() };
        TwitchPredictionsApi api = Build(transport, TwitchScopes.ChannelManagePredictions);
        CreatePredictionRequest request = new(
            "Who wins?",
            [new CreatePredictionOutcome("Blue"), new CreatePredictionOutcome("Pink")],
            120
        );

        Result<TwitchPrediction> result = await api.CreatePredictionAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("pred-1");
        result.Value.Outcomes.Should().HaveCount(2);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("predictions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Query.Should().BeNull();
        transport
            .LastRequest.Body.Should()
            .BeEquivalentTo(
                new
                {
                    BroadcasterId = TwitchId,
                    Title = "Who wins?",
                    Outcomes = request.Outcomes,
                    PredictionWindow = 120,
                }
            );
    }

    [Fact]
    public async Task EndPrediction_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPredictionsApi api = Build(transport); // no scopes granted

        Result<TwitchPrediction> result = await api.EndPredictionAsync(
            Tenant,
            "pred-1",
            "RESOLVED",
            "out-1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EndPrediction_Resolved_BuildsUserTokenPatch_WithFullBody_MapsDto()
    {
        CapturingHelixTransport transport = new() { SingleResult = SamplePrediction("RESOLVED") };
        TwitchPredictionsApi api = Build(transport, TwitchScopes.ChannelManagePredictions);

        Result<TwitchPrediction> result = await api.EndPredictionAsync(
            Tenant,
            "pred-1",
            "RESOLVED",
            "out-1"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("RESOLVED");
        result.Value.WinningOutcomeId.Should().Be("out-1");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("predictions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Query.Should().BeNull();
        transport
            .LastRequest.Body.Should()
            .BeEquivalentTo(
                new
                {
                    BroadcasterId = TwitchId,
                    Id = "pred-1",
                    Status = "RESOLVED",
                    WinningOutcomeId = "out-1",
                }
            );
    }

    [Fact]
    public async Task EndPrediction_Canceled_OmitsWinningOutcome_FromBody()
    {
        CapturingHelixTransport transport = new() { SingleResult = SamplePrediction("CANCELED") };
        TwitchPredictionsApi api = Build(transport, TwitchScopes.ChannelManagePredictions);

        Result<TwitchPrediction> result = await api.EndPredictionAsync(
            Tenant,
            "pred-1",
            "CANCELED",
            null
        );

        result.IsSuccess.Should().BeTrue();
        transport
            .LastRequest!.Body.Should()
            .BeEquivalentTo(
                new
                {
                    BroadcasterId = TwitchId,
                    Id = "pred-1",
                    Status = "CANCELED",
                    WinningOutcomeId = (string?)null,
                }
            );
    }
}
