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
/// Behavioural tests for the Users sub-client: the lookups build app-token requests with one query param
/// per id/login, the scoped methods gate on the required scope and resolve the tenant Guid to the Twitch
/// user id, and each builds the exact Helix request (verb / path / auth / query / body). The capturing
/// transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchUsersApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "44322889";

    private static TwitchUsersApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchUser User(string id, string login) =>
        new(
            id,
            login,
            "Display",
            "",
            "affiliate",
            "a streamer",
            "https://example.test/profile.png",
            "https://example.test/offline.png",
            42,
            DateTimeOffset.UnixEpoch
        );

    [Fact]
    public async Task GetUsersByIds_BuildsAppTokenRequest_OneIdParamPerUser_MapsList()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchUser> { User("1", "alice"), User("2", "bob") },
        };
        TwitchUsersApi api = Build(transport);

        Result<IReadOnlyList<TwitchUser>> result = await api.GetUsersByIdsAsync(["1", "2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Login.Should().Be("alice");
        result.Value[0].BroadcasterType.Should().Be("affiliate");
        result.Value[0].ViewCount.Should().Be(42);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("users");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "2");
        transport.LastRequest.Query!.Count(q => q.Key == "id").Should().Be(2);
    }

    [Fact]
    public async Task GetUsersByLogins_BuildsAppTokenRequest_OneLoginParamPerUser()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchUser> { User("1", "alice") },
        };
        TwitchUsersApi api = Build(transport);

        Result<IReadOnlyList<TwitchUser>> result = await api.GetUsersByLoginsAsync([
            "alice",
            "bob",
        ]);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("users");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "login" && q.Value == "alice");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "login" && q.Value == "bob");
        transport.LastRequest.Query!.Count(q => q.Key == "login").Should().Be(2);
    }

    [Fact]
    public async Task UpdateDescription_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport); // no scopes granted

        Result<TwitchUser> result = await api.UpdateDescriptionAsync(Tenant, "new bio");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateDescription_WithScope_BuildsUserTokenPut_WithDescriptionQuery_MapsDto()
    {
        CapturingHelixTransport transport = new() { SingleResult = User(TwitchId, "login") };
        TwitchUsersApi api = Build(transport, TwitchScopes.UserEdit);

        Result<TwitchUser> result = await api.UpdateDescriptionAsync(Tenant, "new bio");

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(TwitchId);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("users");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "description" && q.Value == "new bio");
    }

    [Fact]
    public async Task GetBlockList_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport);

        Result<TwitchPage<TwitchBlockedUser>> result = await api.GetBlockListAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBlockList_WithScope_BuildsPagedUserTokenQuery_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchBlockedUser>(
                [new TwitchBlockedUser("9", "troll", "Troll")],
                "cursor",
                0
            ),
        };
        TwitchUsersApi api = Build(transport, TwitchScopes.UserReadBlockedUsers);

        Result<TwitchPage<TwitchBlockedUser>> result = await api.GetBlockListAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.UserLogin.Should().Be("troll");
        result.Value.NextCursor.Should().Be("cursor");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("users/blocks");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task BlockUser_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport);

        Result result = await api.BlockUserAsync(Tenant, "9");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task BlockUser_WithScope_BuildsUserTokenPut_WithTargetAndOptionalParams()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport, TwitchScopes.UserManageBlockedUsers);

        Result result = await api.BlockUserAsync(
            Tenant,
            "9",
            sourceContext: "chat",
            reason: "harassment"
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Put);
        transport.LastRequest.Path.Should().Be("users/blocks");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "target_user_id" && q.Value == "9");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "source_context" && q.Value == "chat");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "reason" && q.Value == "harassment");
    }

    [Fact]
    public async Task BlockUser_WithoutOptionalParams_OmitsThem()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport, TwitchScopes.UserManageBlockedUsers);

        Result result = await api.BlockUserAsync(Tenant, "9");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().ContainSingle();
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "target_user_id" && q.Value == "9");
    }

    [Fact]
    public async Task UnblockUser_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport);

        Result result = await api.UnblockUserAsync(Tenant, "9");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UnblockUser_WithScope_BuildsUserTokenDelete_WithTargetParam()
    {
        CapturingHelixTransport transport = new();
        TwitchUsersApi api = Build(transport, TwitchScopes.UserManageBlockedUsers);

        Result result = await api.UnblockUserAsync(Tenant, "9");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("users/blocks");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "target_user_id" && q.Value == "9");
    }
}
