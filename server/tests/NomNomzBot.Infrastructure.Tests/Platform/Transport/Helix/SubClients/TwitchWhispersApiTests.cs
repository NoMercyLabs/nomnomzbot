// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// Behavioural tests for the Whispers sub-client: Send Whisper resolves the tenant Guid to its Twitch id
/// for <c>from_user_id</c>, gates on <c>user:manage:whispers</c>, and builds the exact Helix request
/// (verb / path / auth / query / body). The capturing transport lets us assert the request shape and the
/// short-circuit paths with no HTTP.
/// </summary>
public class TwitchWhispersApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";
    private const string TargetTwitchId = "12826";

    private static TwitchWhispersApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    [Fact]
    public async Task SendWhisper_WithScope_BuildsUserTokenPost_WithFromToIds_AndBody()
    {
        CapturingHelixTransport transport = new();
        TwitchWhispersApi api = Build(transport, TwitchScopes.UserManageWhispers);

        Result result = await api.SendWhisperAsync(Tenant, TargetTwitchId, "hello there");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("whispers");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "from_user_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "to_user_id" && q.Value == TargetTwitchId);
        transport
            .LastRequest.Body.Should()
            .BeOfType<SendWhisperRequest>()
            .Which.Message.Should()
            .Be("hello there");
    }

    [Fact]
    public async Task SendWhisper_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchWhispersApi api = Build(transport); // no scopes granted

        Result result = await api.SendWhisperAsync(Tenant, TargetTwitchId, "hello");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendWhisper_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchWhispersApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.UserManageWhispers)
        );

        Result result = await api.SendWhisperAsync(Tenant, TargetTwitchId, "hello");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsBot_WithBotScope_PostsFromTheBot_OnTheBotToken()
    {
        CapturingHelixTransport transport = new();
        TwitchWhispersApi api = Build(transport, TwitchScopes.UserManageWhispers);

        Result result = await api.SendAsBotAsync(Tenant, "bot1", TargetTwitchId, "queue");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Path.Should().Be("whispers");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "from_user_id" && q.Value == "bot1");
    }

    [Fact]
    public async Task SendAsBot_MissingBotScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchWhispersApi api = Build(transport); // the bot has no user:manage:whispers

        Result result = await api.SendAsBotAsync(Tenant, "bot1", TargetTwitchId, "queue");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }
}
