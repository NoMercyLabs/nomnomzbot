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
/// Behavioural tests for the Goals sub-client: the method gates on the required scope, resolves the
/// tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchGoalsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchGoalsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetCreatorGoals_WithScope_ResolvesTenant_BuildsUserTokenRequest_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchCreatorGoal>
            {
                new(
                    "1",
                    TwitchId,
                    "Name",
                    "login",
                    "subscription",
                    "Sub goal",
                    250,
                    500,
                    DateTimeOffset.UnixEpoch
                ),
            },
        };
        TwitchGoalsApi api = Build(transport, TwitchScopes.ChannelReadGoals);

        Result<IReadOnlyList<TwitchCreatorGoal>> result = await api.GetCreatorGoalsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        TwitchCreatorGoal goal = result.Value.Should().ContainSingle().Subject;
        goal.Type.Should().Be("subscription");
        goal.Description.Should().Be("Sub goal");
        goal.CurrentAmount.Should().Be(250);
        goal.TargetAmount.Should().Be(500);
        goal.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("goals");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetCreatorGoals_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchGoalsApi api = Build(transport); // no scopes granted

        Result<IReadOnlyList<TwitchCreatorGoal>> result = await api.GetCreatorGoalsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCreatorGoals_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchGoalsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadGoals)
        );

        Result<IReadOnlyList<TwitchCreatorGoal>> result = await api.GetCreatorGoalsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }
}
