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
/// Behavioural tests for the Raids sub-client: each method gates on <c>channel:manage:raids</c>, resolves the
/// tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query). Start a Raid
/// carries the resolved tenant as <c>from_broadcaster_id</c> and the raw target id as <c>to_broadcaster_id</c>;
/// Cancel a Raid is status-only on <c>broadcaster_id</c>. The capturing transport asserts shape with no HTTP.
/// </summary>
public class TwitchRaidsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";
    private const string TargetTwitchId = "12345678";

    private static TwitchRaidsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task StartRaid_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchRaidsApi api = Build(transport); // no scopes granted

        Result<TwitchRaid> result = await api.StartRaidAsync(Tenant, TargetTwitchId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartRaid_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchRaidsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelManageRaids)
        );

        Result<TwitchRaid> result = await api.StartRaidAsync(Tenant, TargetTwitchId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartRaid_WithScope_BuildsUserTokenPost_WithFromAndToIds_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchRaid(DateTimeOffset.UnixEpoch, true),
        };
        TwitchRaidsApi api = Build(transport, TwitchScopes.ChannelManageRaids);

        Result<TwitchRaid> result = await api.StartRaidAsync(Tenant, TargetTwitchId);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        result.Value.IsMature.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("raids");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "from_broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "to_broadcaster_id" && q.Value == TargetTwitchId);
    }

    [Fact]
    public async Task CancelRaid_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchRaidsApi api = Build(transport); // no scopes granted

        Result result = await api.CancelRaidAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelRaid_WithScope_BuildsUserTokenDelete_OnBroadcasterId()
    {
        CapturingHelixTransport transport = new();
        TwitchRaidsApi api = Build(transport, TwitchScopes.ChannelManageRaids);

        Result result = await api.CancelRaidAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("raids");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }
}
