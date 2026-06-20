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
/// Behavioural tests for the Ads sub-client: each method gates on its required user-token scope, resolves the
/// tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query / body).
/// Start Commercial posts the length and the resolved channel id in the body; Get Ad Schedule reads a single
/// object on <c>broadcaster_id</c>; Snooze Next Ad posts on <c>broadcaster_id</c>. The capturing transport
/// asserts request shape and short-circuit paths with no HTTP.
/// </summary>
public class TwitchAdsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchAdsApi Build(CapturingHelixTransport transport, params string[] scopes) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task StartCommercial_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchAdsApi api = Build(transport); // no scopes granted

        Result<TwitchCommercial> result = await api.StartCommercialAsync(Tenant, 60);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartCommercial_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchAdsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelEditCommercial)
        );

        Result<TwitchCommercial> result = await api.StartCommercialAsync(Tenant, 60);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartCommercial_WithScope_BuildsUserTokenPost_WithLengthAndResolvedChannelInBody_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchCommercial(60, "Commercial started.", 480),
        };
        TwitchAdsApi api = Build(transport, TwitchScopes.ChannelEditCommercial);

        Result<TwitchCommercial> result = await api.StartCommercialAsync(Tenant, 60);

        result.IsSuccess.Should().BeTrue();
        result.Value.Length.Should().Be(60);
        result.Value.Message.Should().Be("Commercial started.");
        result.Value.RetryAfter.Should().Be(480);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("channels/commercial");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeOfType<StartCommercialRequest>();
        StartCommercialRequest body = (StartCommercialRequest)transport.LastRequest.Body!;
        body.Length.Should().Be(60);
        body.BroadcasterId.Should().Be(TwitchId);
    }

    [Fact]
    public async Task GetAdSchedule_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchAdsApi api = Build(transport); // no scopes granted

        Result<TwitchAdSchedule> result = await api.GetAdScheduleAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAdSchedule_WithScope_BuildsUserTokenGet_OnBroadcasterId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchAdSchedule(
                SnoozeCount: 3,
                SnoozeRefreshAt: 1700000000,
                NextAdAt: 1700003600,
                Duration: 60,
                LastAdAt: 1699996400,
                PrerollFreeTime: 90
            ),
        };
        TwitchAdsApi api = Build(transport, TwitchScopes.ChannelReadAds);

        Result<TwitchAdSchedule> result = await api.GetAdScheduleAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.SnoozeCount.Should().Be(3);
        result.Value.NextAdAt.Should().Be(1700003600);
        result.Value.Duration.Should().Be(60);
        result.Value.PrerollFreeTime.Should().Be(90);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channels/ads");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task SnoozeNextAd_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchAdsApi api = Build(transport); // no scopes granted

        Result<TwitchAdSnooze> result = await api.SnoozeNextAdAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SnoozeNextAd_WithScope_BuildsUserTokenPost_OnBroadcasterId_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchAdSnooze(
                SnoozeCount: 2,
                SnoozeRefreshAt: 1700000000,
                NextAdAt: 1700003900
            ),
        };
        TwitchAdsApi api = Build(transport, TwitchScopes.ChannelManageAds);

        Result<TwitchAdSnooze> result = await api.SnoozeNextAdAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.SnoozeCount.Should().Be(2);
        result.Value.SnoozeRefreshAt.Should().Be(1700000000);
        result.Value.NextAdAt.Should().Be(1700003900);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("channels/ads/schedule/snooze");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }
}
