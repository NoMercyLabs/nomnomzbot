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
/// Behavioural tests for the Teams sub-client. <c>GetChannelTeams</c> resolves the tenant Guid to its Twitch
/// id and builds the App-token request (verb / path / auth / query), short-circuiting with <c>not_found</c>
/// for an unknown tenant; <c>GetTeams</c> is a public App-token read keyed on a team name/id, so it neither
/// resolves a tenant nor gates a scope. The capturing transport proves the request shape and DTO mapping with
/// no HTTP.
/// </summary>
public class TwitchTeamsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchTeamsApi Build(CapturingHelixTransport transport) =>
        new(transport, new StubIdentityResolver(Tenant, TwitchId));

    [Fact]
    public async Task GetChannelTeams_ResolvesTenant_BuildsAppTokenRequest_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChannelTeam>
            {
                new(
                    "6358",
                    "staff",
                    "Twitch Staff",
                    TwitchId,
                    "login",
                    "Name",
                    "https://bg/image.png",
                    "https://banner/image.png",
                    "We are Twitch.",
                    "https://thumb/image.png",
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch.AddDays(1)
                ),
            },
        };
        TwitchTeamsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChannelTeam>> result = await api.GetChannelTeamsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        TwitchChannelTeam team = result.Value.Should().ContainSingle().Subject;
        team.Id.Should().Be("6358");
        team.TeamName.Should().Be("staff");
        team.TeamDisplayName.Should().Be("Twitch Staff");
        team.BroadcasterId.Should().Be(TwitchId);
        team.Info.Should().Be("We are Twitch.");
        team.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        team.UpdatedAt.Should().Be(DateTimeOffset.UnixEpoch.AddDays(1));

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("teams/channel");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetChannelTeams_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchTeamsApi api = new(transport, new StubIdentityResolver(Guid.NewGuid(), "other"));

        Result<IReadOnlyList<TwitchChannelTeam>> result = await api.GetChannelTeamsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTeams_ByName_BuildsAppTokenRequest_MapsTeamWithMembers()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchTeam(
                "6358",
                "staff",
                "Twitch Staff",
                [
                    new TwitchTeamMember("1234", "dallas", "Dallas"),
                    new TwitchTeamMember("5678", "cooler", "Cooler"),
                ],
                "https://bg/image.png",
                "https://banner/image.png",
                "We are Twitch.",
                "https://thumb/image.png",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddDays(1)
            ),
        };
        TwitchTeamsApi api = Build(transport);

        Result<TwitchTeam> result = await api.GetTeamsAsync("staff", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("6358");
        result.Value.TeamName.Should().Be("staff");
        result.Value.Users.Should().HaveCount(2);
        result.Value.Users[0].UserLogin.Should().Be("dallas");
        result.Value.Users[1].UserName.Should().Be("Cooler");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("teams");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "name" && q.Value == "staff");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "id");
    }

    [Fact]
    public async Task GetTeams_ById_SendsIdQuery_WithoutResolvingTenant()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchTeam(
                "6358",
                "staff",
                "Twitch Staff",
                [],
                "https://bg/image.png",
                "https://banner/image.png",
                "We are Twitch.",
                "https://thumb/image.png",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch
            ),
        };
        TwitchTeamsApi api = Build(transport);

        Result<TwitchTeam> result = await api.GetTeamsAsync(null, "6358");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Path.Should().Be("teams");
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport.LastRequest.Query.Should().ContainSingle(q => q.Key == "id" && q.Value == "6358");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "name");
    }
}
