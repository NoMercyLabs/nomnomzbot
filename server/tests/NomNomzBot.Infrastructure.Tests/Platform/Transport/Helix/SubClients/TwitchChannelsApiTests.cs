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
/// Behavioural tests for the Channels sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchChannelsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchChannelsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetChannelInformation_ResolvesTenant_BuildsAppTokenRequest_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchChannelInformation(
                TwitchId,
                "login",
                "Name",
                "en",
                "509658",
                "Software & Game Development",
                "Building a bot",
                0,
                ["coding"],
                [],
                false
            ),
        };
        TwitchChannelsApi api = Build(transport);

        Result<TwitchChannelInformation> result = await api.GetChannelInformationAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.GameName.Should().Be("Software & Game Development");
        result.Value.Tags.Should().ContainSingle().Which.Should().Be("coding");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetChannelInformation_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchChannelInformation> result = await api.GetChannelInformationAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    private static TwitchChannelInformation Info(string id, string gameName) =>
        new(id, "login" + id, "Name" + id, "en", "1", gameName, "t", 0, [], [], false);

    [Fact]
    public async Task GetChannelInformationByTwitchIds_SendsRawIdsAsBroadcasterId_WithAppToken_MapsGameName()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChannelInformation>
            {
                Info("7", "Just Chatting"),
                Info("8", "Chess"),
            },
        };
        TwitchChannelsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChannelInformation>> result =
            await api.GetChannelInformationByTwitchIdsAsync(["7", "8"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(i => i.GameName).Should().Equal("Just Chatting", "Chess");
        transport.CallCount.Should().Be(1);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query!.Where(q => q.Key == "broadcaster_id")
            .Select(q => q.Value)
            .Should()
            .Equal("7", "8");
    }

    [Fact]
    public async Task GetChannelInformationByTwitchIds_MoreThan100Ids_AreSplitIntoCallsOf100()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchChannelInformation>(),
        };
        TwitchChannelsApi api = Build(transport);
        List<string> ids = [.. Enumerable.Range(1, 150).Select(i => i.ToString())];

        Result<IReadOnlyList<TwitchChannelInformation>> result =
            await api.GetChannelInformationByTwitchIdsAsync(ids);

        result.IsSuccess.Should().BeTrue();
        transport.Requests.Select(r => r.Query!.Count).Should().Equal(100, 50);
    }

    [Fact]
    public async Task GetChannelInformationByTwitchIds_NoIds_ReturnsEmpty_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChannelInformation>> result =
            await api.GetChannelInformationByTwitchIdsAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ModifyChannelInformation_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelsApi api = Build(transport); // no scopes granted

        Result result = await api.ModifyChannelInformationAsync(Tenant, new(Title: "new"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ModifyChannelInformation_WithScope_BuildsUserTokenPatch_WithBody()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelsApi api = Build(transport, TwitchScopes.ChannelManageBroadcast);
        ModifyChannelInformationRequest request = new(Title: "new title", GameId: "509658");

        Result result = await api.ModifyChannelInformationAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("channels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetChannelEditors_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelsApi api = Build(transport);

        Result<IReadOnlyList<TwitchChannelEditor>> result = await api.GetChannelEditorsAsync(
            Tenant
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetChannelFollowers_WithScope_BuildsPagedQuery()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchChannelFollower>(
                [new("1", "a", "A", DateTimeOffset.UnixEpoch)],
                "cursor",
                5
            ),
        };
        TwitchChannelsApi api = Build(transport, TwitchScopes.ModeratorReadFollowers);

        Result<TwitchPage<TwitchChannelFollower>> result = await api.GetChannelFollowersAsync(
            Tenant,
            new(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(5);
        result.Value.NextCursor.Should().Be("cursor");
        transport.LastRequest!.Path.Should().Be("channels/followers");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetChannelFollowerCount_RequestsFirstOne_ReturnsTotal()
    {
        CapturingHelixTransport transport = new() { TotalResult = 1234 };
        TwitchChannelsApi api = Build(transport, TwitchScopes.ModeratorReadFollowers);

        Result<int> result = await api.GetChannelFollowerCountAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1234);
        transport.LastRequest!.Query.Should().Contain(q => q.Key == "first" && q.Value == "1");
    }
}
