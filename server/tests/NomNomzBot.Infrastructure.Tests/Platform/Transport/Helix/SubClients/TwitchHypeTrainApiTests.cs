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
/// Behavioural tests for the Hype Train sub-client: the method resolves the tenant Guid to the Twitch id,
/// gates on <c>channel:read:hype_train</c>, and builds the exact Helix request (verb / path / auth / query),
/// then maps the nested status DTO. The capturing transport lets us assert the request shape and the
/// short-circuit paths with no HTTP.
/// </summary>
public class TwitchHypeTrainApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchHypeTrainApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetHypeTrainStatus_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchHypeTrainApi api = Build(transport); // no scopes granted

        Result<TwitchHypeTrainStatus> result = await api.GetHypeTrainStatusAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHypeTrainStatus_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchHypeTrainApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadHypeTrain)
        );

        Result<TwitchHypeTrainStatus> result = await api.GetHypeTrainStatusAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHypeTrainStatus_WithScope_ResolvesTenant_BuildsUserTokenGet_MapsNestedDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchHypeTrainStatus(
                new TwitchHypeTrain(
                    "1b0AsbInCHZW2SQFQkCzqN07Ib2",
                    "1337",
                    "cool_user",
                    "Cool_User",
                    2,
                    700,
                    200,
                    1000,
                    [
                        new TwitchHypeTrainContribution("123", "pogchamp", "PogChamp", "bits", 50),
                        new TwitchHypeTrainContribution(
                            "456",
                            "kappa",
                            "Kappa",
                            "subscription",
                            45
                        ),
                    ],
                    [new TwitchHypeTrainParticipant("456", "pogchamp", "PogChamp")],
                    DateTimeOffset.Parse("2020-07-15T17:16:03.17106713Z"),
                    DateTimeOffset.Parse("2020-07-15T17:16:11.17106713Z"),
                    "golden_kappa",
                    true
                ),
                new TwitchHypeTrainRecord(
                    6,
                    2850,
                    DateTimeOffset.Parse("2020-04-24T20:12:21.003802269Z")
                ),
                new TwitchHypeTrainRecord(
                    16,
                    23850,
                    DateTimeOffset.Parse("2020-04-27T20:12:21.003802269Z")
                )
            ),
        };
        TwitchHypeTrainApi api = Build(transport, TwitchScopes.ChannelReadHypeTrain);

        Result<TwitchHypeTrainStatus> result = await api.GetHypeTrainStatusAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Current!.Level.Should().Be(2);
        result.Value.Current.Goal.Should().Be(1000);
        result.Value.Current.Type.Should().Be("golden_kappa");
        result.Value.Current.IsSharedTrain.Should().BeTrue();
        result.Value.Current.TopContributions.Should().HaveCount(2);
        result
            .Value.Current.TopContributions[0]
            .Should()
            .BeEquivalentTo(
                new TwitchHypeTrainContribution("123", "pogchamp", "PogChamp", "bits", 50)
            );
        result.Value.Current.SharedTrainParticipants.Should().ContainSingle();
        result.Value.AllTimeHigh!.Total.Should().Be(2850);
        result.Value.SharedAllTimeHigh!.Level.Should().Be(16);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("hypetrain/status");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetHypeTrainStatus_NoActiveTrain_MapsNullCurrent()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchHypeTrainStatus(
                null,
                new TwitchHypeTrainRecord(
                    6,
                    2850,
                    DateTimeOffset.Parse("2020-04-24T20:12:21.003802269Z")
                ),
                null
            ),
        };
        TwitchHypeTrainApi api = Build(transport, TwitchScopes.ChannelReadHypeTrain);

        Result<TwitchHypeTrainStatus> result = await api.GetHypeTrainStatusAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Current.Should().BeNull();
        result.Value.AllTimeHigh!.Level.Should().Be(6);
        result.Value.SharedAllTimeHigh.Should().BeNull();
    }
}
