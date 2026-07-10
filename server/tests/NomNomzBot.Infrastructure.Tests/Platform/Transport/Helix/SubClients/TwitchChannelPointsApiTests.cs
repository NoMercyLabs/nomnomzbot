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
/// Behavioural tests for the Channel Points sub-client: each method resolves the tenant Guid to the Twitch
/// id, gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchChannelPointsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "44322889";

    private static TwitchChannelPointsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchCustomReward SampleReward(string id = "reward-1", int cost = 500) =>
        new(
            TwitchId,
            "login",
            "Name",
            id,
            "Hydrate",
            "Make me drink water",
            cost,
            new TwitchCustomRewardImage("1x", "2x", "4x"),
            new TwitchCustomRewardImage("d1x", "d2x", "d4x"),
            "#9146FF",
            true, // IsEnabled
            true, // IsUserInputRequired
            new TwitchCustomRewardMaxPerStreamSetting(false, 0),
            new TwitchCustomRewardMaxPerUserPerStreamSetting(false, 0),
            new TwitchCustomRewardGlobalCooldownSetting(true, 60),
            false,
            true,
            false,
            3,
            DateTimeOffset.UnixEpoch
        );

    private static TwitchCustomRewardRedemption SampleRedemption(
        string id = "redemption-1",
        string status = "UNFULFILLED"
    ) =>
        new(
            TwitchId,
            "login",
            "Name",
            id,
            "9999",
            "Viewer",
            "viewer",
            new TwitchRedemptionReward("reward-1", "Hydrate", "Make me drink water", 500),
            "please",
            status,
            DateTimeOffset.UnixEpoch
        );

    [Fact]
    public async Task CreateCustomReward_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport); // no scopes granted

        Result<TwitchCustomReward> result = await api.CreateCustomRewardAsync(
            Tenant,
            new CreateCustomRewardRequest("Hydrate", 500)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateCustomReward_WithScope_BuildsUserTokenPost_WithBody_MapsReward()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleReward(cost: 750) };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelManageRedemptions);
        CreateCustomRewardRequest request = new("Hydrate", 750, Prompt: "drink");

        Result<TwitchCustomReward> result = await api.CreateCustomRewardAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Cost.Should().Be(750);
        result.Value.GlobalCooldownSetting.GlobalCooldownSeconds.Should().Be(60);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateCustomReward_WithScope_BuildsUserTokenPatch_WithIdAndBody()
    {
        CapturingHelixTransport transport = new() { SingleResult = SampleReward() };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelManageRedemptions);
        UpdateCustomRewardRequest request = new(IsPaused: true);

        Result<TwitchCustomReward> result = await api.UpdateCustomRewardAsync(
            Tenant,
            "reward-1",
            request
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "reward-1");
    }

    [Fact]
    public async Task UpdateCustomReward_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport);

        Result<TwitchCustomReward> result = await api.UpdateCustomRewardAsync(
            Tenant,
            "reward-1",
            new UpdateCustomRewardRequest(Cost: 100)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteCustomReward_WithScope_BuildsUserTokenDelete_WithBroadcasterAndId()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelManageRedemptions);

        Result result = await api.DeleteCustomRewardAsync(Tenant, "reward-1");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "reward-1");
    }

    [Fact]
    public async Task DeleteCustomReward_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport);

        Result result = await api.DeleteCustomRewardAsync(Tenant, "reward-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCustomRewards_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport);

        Result<IReadOnlyList<TwitchCustomReward>> result = await api.GetCustomRewardsAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCustomRewards_WithScope_BuildsGet_WithRepeatedIds_AndManageableFlag()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchCustomReward> { SampleReward("a"), SampleReward("b") },
        };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelReadRedemptions);

        Result<IReadOnlyList<TwitchCustomReward>> result = await api.GetCustomRewardsAsync(
            Tenant,
            ["a", "b"],
            onlyManageableRewards: true
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("a");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "a");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "b");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "only_manageable_rewards" && q.Value == "true");
    }

    [Fact]
    public async Task GetCustomRewards_WithScope_NoFilters_OmitsOptionalQuery()
    {
        CapturingHelixTransport transport = new() { ListResult = new List<TwitchCustomReward>() };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelReadRedemptions);

        Result<IReadOnlyList<TwitchCustomReward>> result = await api.GetCustomRewardsAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        transport
            .LastRequest!.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetCustomRewardRedemptions_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport);

        Result<TwitchPage<TwitchCustomRewardRedemption>> result =
            await api.GetCustomRewardRedemptionsAsync(
                Tenant,
                "reward-1",
                "UNFULFILLED",
                null,
                null,
                new TwitchPageRequest()
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCustomRewardRedemptions_ByStatus_BuildsPagedQuery_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchCustomRewardRedemption>(
                [SampleRedemption()],
                "cursor",
                7
            ),
        };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelReadRedemptions);

        Result<TwitchPage<TwitchCustomRewardRedemption>> result =
            await api.GetCustomRewardRedemptionsAsync(
                Tenant,
                "reward-1",
                "UNFULFILLED",
                null,
                "NEWEST",
                new TwitchPageRequest(After: "abc", PageSize: 25)
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(7);
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle().Which.Status.Should().Be("UNFULFILLED");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards/redemptions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "reward_id" && q.Value == "reward-1");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "status" && q.Value == "UNFULFILLED");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "sort" && q.Value == "NEWEST");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetCustomRewardRedemptions_ById_BuildsRepeatedIdQuery()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchCustomRewardRedemption>(
                [SampleRedemption("r1"), SampleRedemption("r2")],
                null,
                2
            ),
        };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelReadRedemptions);

        Result<TwitchPage<TwitchCustomRewardRedemption>> result =
            await api.GetCustomRewardRedemptionsAsync(
                Tenant,
                "reward-1",
                null,
                ["r1", "r2"],
                null,
                new TwitchPageRequest()
            );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().Contain(q => q.Key == "id" && q.Value == "r1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "r2");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "status");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "sort");
    }

    [Fact]
    public async Task UpdateRedemptionStatus_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchChannelPointsApi api = Build(transport);

        Result<IReadOnlyList<TwitchCustomRewardRedemption>> result =
            await api.UpdateRedemptionStatusAsync(
                Tenant,
                "reward-1",
                ["r1"],
                new UpdateRedemptionStatusRequest("FULFILLED")
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRedemptionStatus_WithScope_BuildsUserTokenPatch_WithIdsAndBody_MapsList()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchCustomRewardRedemption>
            {
                SampleRedemption("r1", "FULFILLED"),
                SampleRedemption("r2", "FULFILLED"),
            },
        };
        TwitchChannelPointsApi api = Build(transport, TwitchScopes.ChannelManageRedemptions);
        UpdateRedemptionStatusRequest request = new("FULFILLED");

        Result<IReadOnlyList<TwitchCustomRewardRedemption>> result =
            await api.UpdateRedemptionStatusAsync(Tenant, "reward-1", ["r1", "r2"], request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(r => r.Status == "FULFILLED");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("channel_points/custom_rewards/redemptions");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "reward_id" && q.Value == "reward-1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "r1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "r2");
    }
}
