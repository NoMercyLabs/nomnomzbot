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
/// Behavioural tests for the Subscriptions sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query). The capturing
/// transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchSubscriptionsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "141981764";

    private static TwitchSubscriptionsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetBroadcasterSubscriptions_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchSubscriptionsApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchBroadcasterSubscription>> result =
            await api.GetBroadcasterSubscriptionsAsync(Tenant, null, new TwitchPageRequest());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBroadcasterSubscriptions_WithScope_BuildsUserTokenPagedQuery_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchBroadcasterSubscription>(
                [
                    new TwitchBroadcasterSubscription(
                        TwitchId,
                        "broadcasterlogin",
                        "BroadcasterName",
                        "",
                        "",
                        "",
                        false,
                        "Channel Subscription (broadcasterlogin)",
                        "1000",
                        "27419011",
                        "subscriberlogin",
                        "SubscriberName"
                    ),
                ],
                "cursor",
                42
            ),
        };
        TwitchSubscriptionsApi api = Build(transport, TwitchScopes.ChannelReadSubscriptions);

        Result<TwitchPage<TwitchBroadcasterSubscription>> result =
            await api.GetBroadcasterSubscriptionsAsync(
                Tenant,
                null,
                new TwitchPageRequest(After: "abc", PageSize: 50)
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(42);
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Tier.Should().Be("1000");
        result.Value.Items[0].UserId.Should().Be("27419011");
        result.Value.Items[0].IsGift.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("subscriptions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetBroadcasterSubscriptions_WithUserFilter_RepeatsUserIdQueryPerId()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchBroadcasterSubscription>([], null, 0),
        };
        TwitchSubscriptionsApi api = Build(transport, TwitchScopes.ChannelReadSubscriptions);

        Result<TwitchPage<TwitchBroadcasterSubscription>> result =
            await api.GetBroadcasterSubscriptionsAsync(
                Tenant,
                ["100", "200"],
                new TwitchPageRequest()
            );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().Contain(q => q.Key == "user_id" && q.Value == "100");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "user_id" && q.Value == "200");
    }

    [Fact]
    public async Task GetBroadcasterSubscriptions_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchSubscriptionsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadSubscriptions)
        );

        Result<TwitchPage<TwitchBroadcasterSubscription>> result =
            await api.GetBroadcasterSubscriptionsAsync(Tenant, null, new TwitchPageRequest());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckUserSubscription_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchSubscriptionsApi api = Build(transport); // no scopes granted

        Result<TwitchUserSubscription> result = await api.CheckUserSubscriptionAsync(
            Tenant,
            "27419011"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckUserSubscription_WithScope_BuildsUserTokenSingleRequest_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchUserSubscription(
                TwitchId,
                "broadcasterlogin",
                "BroadcasterName",
                true,
                "2000",
                "141981764",
                "gifterlogin",
                "GifterName"
            ),
        };
        TwitchSubscriptionsApi api = Build(transport, TwitchScopes.UserReadSubscriptions);

        Result<TwitchUserSubscription> result = await api.CheckUserSubscriptionAsync(
            Tenant,
            "27419011"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().Be("2000");
        result.Value.IsGift.Should().BeTrue();
        result.Value.GifterName.Should().Be("GifterName");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("subscriptions/user");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == "27419011");
    }

    [Fact]
    public async Task GetSubscriberCount_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchSubscriptionsApi api = Build(transport); // no scopes granted

        Result<int> result = await api.GetSubscriberCountAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSubscriberCount_WithScope_RequestsFirstOne_ReturnsTotal()
    {
        CapturingHelixTransport transport = new() { TotalResult = 256 };
        TwitchSubscriptionsApi api = Build(transport, TwitchScopes.ChannelReadSubscriptions);

        Result<int> result = await api.GetSubscriberCountAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(256);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("subscriptions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "1");
    }
}
