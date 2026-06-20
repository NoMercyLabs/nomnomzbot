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
/// Behavioural tests for the Moderators &amp; VIPs sub-client: each method resolves the tenant Guid to the
/// Twitch id, gates on the required scope, and builds the exact Helix request (verb / path / auth / query).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchModeratorsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";
    private const string TargetUserId = "98765432";

    private static TwitchModeratorsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetModerators_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchModerator>> result = await api.GetModeratorsAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetModerators_WithScope_BuildsPagedUserTokenQuery_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchModerator>(
                [new TwitchModerator("1", "modlogin", "ModName")],
                "cursor",
                0
            ),
        };
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ModerationRead);

        Result<TwitchPage<TwitchModerator>> result = await api.GetModeratorsAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle().Which.UserName.Should().Be("ModName");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/moderators");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetModerators_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ModerationRead)
        );

        Result<TwitchPage<TwitchModerator>> result = await api.GetModeratorsAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AddModerator_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport);

        Result result = await api.AddModeratorAsync(Tenant, TargetUserId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AddModerator_WithScope_BuildsUserTokenPost_WithBroadcasterAndUserQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ChannelManageModerators);

        Result result = await api.AddModeratorAsync(Tenant, TargetUserId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("moderation/moderators");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetUserId);
    }

    [Fact]
    public async Task RemoveModerator_WithScope_BuildsUserTokenDelete_WithBroadcasterAndUserQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ChannelManageModerators);

        Result result = await api.RemoveModeratorAsync(Tenant, TargetUserId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("moderation/moderators");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetUserId);
    }

    [Fact]
    public async Task GetVips_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport);

        Result<TwitchPage<TwitchVip>> result = await api.GetVipsAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetVips_WithScope_BuildsPagedUserTokenQuery_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchVip>(
                [new TwitchVip("7", "VipName", "viplogin")],
                "next",
                0
            ),
        };
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ChannelReadVips);

        Result<TwitchPage<TwitchVip>> result = await api.GetVipsAsync(
            Tenant,
            new TwitchPageRequest(After: "cur", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("next");
        result.Value.Items.Should().ContainSingle().Which.UserLogin.Should().Be("viplogin");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channels/vips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task AddVip_WithScope_BuildsUserTokenPost_WithBroadcasterAndUserQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ChannelManageVips);

        Result result = await api.AddVipAsync(Tenant, TargetUserId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("channels/vips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetUserId);
    }

    [Fact]
    public async Task RemoveVip_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport);

        Result result = await api.RemoveVipAsync(Tenant, TargetUserId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveVip_WithScope_BuildsUserTokenDelete_WithBroadcasterAndUserQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport, TwitchScopes.ChannelManageVips);

        Result result = await api.RemoveVipAsync(Tenant, TargetUserId);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("channels/vips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TargetUserId);
    }

    [Fact]
    public async Task GetModeratedChannels_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchModeratorsApi api = Build(transport);

        Result<TwitchPage<TwitchModeratedChannel>> result = await api.GetModeratedChannelsAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetModeratedChannels_WithScope_ResolvesUserId_BuildsPagedUserTokenQuery_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchModeratedChannel>(
                [new TwitchModeratedChannel("11", "chanlogin", "ChanName")],
                "cur2",
                0
            ),
        };
        TwitchModeratorsApi api = Build(transport, TwitchScopes.UserReadModeratedChannels);

        Result<TwitchPage<TwitchModeratedChannel>> result = await api.GetModeratedChannelsAsync(
            Tenant,
            new TwitchPageRequest(After: "p", PageSize: 100)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cur2");
        result.Value.Items.Should().ContainSingle().Which.BroadcasterName.Should().Be("ChanName");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("moderation/channels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "100");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "p");
    }
}
