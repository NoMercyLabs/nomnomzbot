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
/// Behavioural tests for the Bits sub-client: each method gates on <c>bits:read</c> where required, resolves
/// the tenant Guid to the Twitch id, and builds the exact Helix request (verb / path / auth / query). The
/// capturing transport lets us assert the request shape, the short-circuit paths, and the DTO mapping —
/// including the dictionary-modelled cheermote image scales — with no HTTP.
/// </summary>
public class TwitchBitsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "141981764";

    private static TwitchBitsApi Build(CapturingHelixTransport transport, params string[] scopes) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task GetBitsLeaderboard_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchBitsApi api = Build(transport); // no scopes granted

        Result<IReadOnlyList<TwitchBitsLeaderboardEntry>> result =
            await api.GetBitsLeaderboardAsync(Tenant, 10, "week", null, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBitsLeaderboard_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchBitsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.BitsRead)
        );

        Result<IReadOnlyList<TwitchBitsLeaderboardEntry>> result =
            await api.GetBitsLeaderboardAsync(Tenant, null, null, null, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBitsLeaderboard_WithScope_BuildsUserTokenRequest_NoBroadcasterIdParam_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchBitsLeaderboardEntry>
            {
                new("12345", "leader", "Leader", 1, 9001),
            },
        };
        TwitchBitsApi api = Build(transport, TwitchScopes.BitsRead);
        DateTimeOffset startedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Result<IReadOnlyList<TwitchBitsLeaderboardEntry>> result =
            await api.GetBitsLeaderboardAsync(Tenant, 5, "month", startedAt, "12345");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].UserName.Should().Be("Leader");
        result.Value[0].Rank.Should().Be(1);
        result.Value[0].Score.Should().Be(9001);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("bits/leaderboard");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.BroadcasterId.Should().Be(Tenant);
        // The leaderboard takes its broadcaster from the token, never a query param.
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "broadcaster_id");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "count" && q.Value == "5");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "period" && q.Value == "month");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "user_id" && q.Value == "12345");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "started_at");
    }

    [Fact]
    public async Task GetBitsLeaderboard_NoOptionalArgs_OmitsAllQueryParams()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchBitsLeaderboardEntry>(),
        };
        TwitchBitsApi api = Build(transport, TwitchScopes.BitsRead);

        Result<IReadOnlyList<TwitchBitsLeaderboardEntry>> result =
            await api.GetBitsLeaderboardAsync(Tenant, null, null, null, null);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCheermotes_WithTenant_ResolvesBroadcasterId_BuildsAppTokenRequest_MapsNestedImages()
    {
        IReadOnlyDictionary<string, string> animatedScales = new Dictionary<string, string>
        {
            ["1"] = "https://cdn/1.gif",
            ["1.5"] = "https://cdn/1.5.gif",
            ["2"] = "https://cdn/2.gif",
            ["3"] = "https://cdn/3.gif",
            ["4"] = "https://cdn/4.gif",
        };
        IReadOnlyDictionary<string, string> staticScales = new Dictionary<string, string>
        {
            ["1"] = "https://cdn/1.png",
            ["1.5"] = "https://cdn/1.5.png",
        };
        TwitchCheermoteImages images = new(
            new TwitchCheermoteImageFormats(
                new TwitchCheermoteImageScales(animatedScales),
                new TwitchCheermoteImageScales(staticScales)
            ),
            new TwitchCheermoteImageFormats(
                new TwitchCheermoteImageScales(animatedScales),
                new TwitchCheermoteImageScales(staticScales)
            )
        );
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchCheermote>
            {
                new(
                    "Cheer",
                    [new TwitchCheermoteTier(1, "1", "#979797", images, true, true)],
                    "global_first_party",
                    1,
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    false
                ),
            },
        };
        TwitchBitsApi api = Build(transport);

        Result<IReadOnlyList<TwitchCheermote>> result = await api.GetCheermotesAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        TwitchCheermote cheermote = result.Value[0];
        cheermote.Prefix.Should().Be("Cheer");
        cheermote.Tiers.Should().ContainSingle();
        cheermote.Tiers[0].MinBits.Should().Be(1);
        // The awkward 1.5 scale survives the dictionary modelling that fixed properties could not name.
        cheermote
            .Tiers[0]
            .Images.Light.Animated.Scales.Should()
            .Contain(s => s.Key == "1.5" && s.Value == "https://cdn/1.5.gif");
        cheermote.Tiers[0].Images.Dark.Static.Scales["1"].Should().Be("https://cdn/1.png");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("bits/cheermotes");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetCheermotes_NoTenant_OmitsBroadcasterId_StillAppToken()
    {
        CapturingHelixTransport transport = new() { ListResult = new List<TwitchCheermote>() };
        TwitchBitsApi api = Build(transport);

        Result<IReadOnlyList<TwitchCheermote>> result = await api.GetCheermotesAsync(null);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Path.Should().Be("bits/cheermotes");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCheermotes_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchBitsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<IReadOnlyList<TwitchCheermote>> result = await api.GetCheermotesAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCustomPowerUps_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchBitsApi api = Build(transport);

        Result<IReadOnlyList<TwitchCustomPowerUp>> result = await api.GetCustomPowerUpsAsync(
            Tenant,
            null
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCustomPowerUps_WithScopeAndIds_BuildsUserTokenRequest_WithBroadcasterAndIdParams_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchCustomPowerUp>
            {
                new(
                    TwitchId,
                    "login",
                    "Name",
                    "powerup-1",
                    "Gigantify",
                    "Make it big",
                    500,
                    new TwitchCustomPowerUpImage("1x", "2x", "4x"),
                    new TwitchCustomPowerUpImage("d1x", "d2x", "d4x"),
                    "#9146FF",
                    true,
                    false,
                    false,
                    true,
                    new TwitchCustomPowerUpMaxPerStreamSetting(true, 10),
                    new TwitchCustomPowerUpMaxPerUserPerStreamSetting(false, 0),
                    new TwitchCustomPowerUpGlobalCooldownSetting(true, 60),
                    3,
                    new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero)
                ),
            },
        };
        TwitchBitsApi api = Build(transport, TwitchScopes.BitsRead);

        Result<IReadOnlyList<TwitchCustomPowerUp>> result = await api.GetCustomPowerUpsAsync(
            Tenant,
            ["powerup-1", "powerup-2"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        TwitchCustomPowerUp powerUp = result.Value[0];
        powerUp.Title.Should().Be("Gigantify");
        powerUp.Bits.Should().Be(500);
        powerUp.Image!.Url1x.Should().Be("1x");
        powerUp.MaxPerStreamSetting.MaxPerStream.Should().Be(10);
        powerUp.GlobalCooldownSetting.GlobalCooldownSeconds.Should().Be(60);
        powerUp.RedemptionsRedeemedCurrentStream.Should().Be(3);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("bits/custom_power_ups");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.BroadcasterId.Should().Be(Tenant);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "powerup-1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "powerup-2");
    }

    [Fact]
    public async Task GetCustomPowerUps_NoIds_OmitsIdParams_KeepsBroadcasterId()
    {
        CapturingHelixTransport transport = new() { ListResult = new List<TwitchCustomPowerUp>() };
        TwitchBitsApi api = Build(transport, TwitchScopes.BitsRead);

        Result<IReadOnlyList<TwitchCustomPowerUp>> result = await api.GetCustomPowerUpsAsync(
            Tenant,
            null
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "id");
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }
}
