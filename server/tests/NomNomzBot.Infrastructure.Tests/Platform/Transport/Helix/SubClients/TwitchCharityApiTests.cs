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
/// Behavioural tests for the Charity sub-client: each method gates on <c>channel:read:charity</c>, resolves
/// the tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query). The
/// capturing transport lets us assert the request shape, the mapped DTO, and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchCharityApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchCharityApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetCharityCampaign_WithScope_BuildsUserTokenRequest_MapsDtoWithNestedAmounts()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchCharityCampaign(
                "123-abc",
                TwitchId,
                "login",
                "Name",
                "Example Charity",
                "Helping people",
                "https://logo",
                "https://charity.example",
                new TwitchCharityAmount(50000, 2, "USD"),
                new TwitchCharityAmount(1000000, 2, "USD")
            ),
        };
        TwitchCharityApi api = Build(transport, TwitchScopes.ChannelReadCharity);

        Result<TwitchCharityCampaign> result = await api.GetCharityCampaignAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.CharityName.Should().Be("Example Charity");
        result.Value.CurrentAmount.Value.Should().Be(50000);
        result.Value.CurrentAmount.DecimalPlaces.Should().Be(2);
        result.Value.CurrentAmount.Currency.Should().Be("USD");
        result.Value.TargetAmount.Value.Should().Be(1000000);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("charity/campaigns");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetCharityCampaign_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchCharityApi api = Build(transport); // no scopes granted

        Result<TwitchCharityCampaign> result = await api.GetCharityCampaignAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCharityCampaign_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchCharityApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadCharity)
        );

        Result<TwitchCharityCampaign> result = await api.GetCharityCampaignAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCharityCampaignDonations_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchCharityApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchCharityDonation>> result =
            await api.GetCharityCampaignDonationsAsync(Tenant, new TwitchPageRequest());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCharityCampaignDonations_WithScope_BuildsPagedQuery_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchCharityDonation>(
                [
                    new TwitchCharityDonation(
                        "d-1",
                        "c-1",
                        "9001",
                        "donor",
                        "Donor",
                        new TwitchCharityAmount(2500, 2, "USD")
                    ),
                ],
                "cursor",
                0
            ),
        };
        TwitchCharityApi api = Build(transport, TwitchScopes.ChannelReadCharity);

        Result<TwitchPage<TwitchCharityDonation>> result =
            await api.GetCharityCampaignDonationsAsync(
                Tenant,
                new TwitchPageRequest(After: "abc", PageSize: 50)
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].CampaignId.Should().Be("c-1");
        result.Value.Items[0].Amount.Value.Should().Be(2500);
        result.Value.Items[0].Amount.Currency.Should().Be("USD");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("charity/donations");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }
}
