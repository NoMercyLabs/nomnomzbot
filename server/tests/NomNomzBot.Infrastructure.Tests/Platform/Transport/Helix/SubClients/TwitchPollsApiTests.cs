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
/// Behavioural tests for the Polls sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body).
/// Create and End put the resolved channel id in the *body* (Twitch's shape for these endpoints), which
/// these tests assert directly off the captured request. The capturing transport lets us check the
/// request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchPollsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchPollsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchPoll SamplePoll(string status = "ACTIVE") =>
        new(
            "ed961efd-8a3f-4cf5-a9d0-e616c590cd2a",
            TwitchId,
            "Name",
            "login",
            "Heads or Tails?",
            [new TwitchPollChoice("choice-1", "Heads", 10, 7, 0)],
            false,
            10,
            true,
            100,
            status,
            300,
            DateTimeOffset.UnixEpoch,
            null
        );

    [Fact]
    public async Task GetPolls_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPollsApi api = Build(transport); // no scopes granted

        Result<TwitchPage<TwitchPoll>> result = await api.GetPollsAsync(
            Tenant,
            null,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPolls_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPollsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.ChannelReadPolls)
        );

        Result<TwitchPage<TwitchPoll>> result = await api.GetPollsAsync(
            Tenant,
            null,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPolls_WithScope_BuildsPagedUserTokenQuery_AndIdFilter_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchPoll>([SamplePoll()], "cursor", 1),
        };
        TwitchPollsApi api = Build(transport, TwitchScopes.ChannelReadPolls);

        Result<TwitchPage<TwitchPoll>> result = await api.GetPollsAsync(
            Tenant,
            ["poll-a", "poll-b"],
            new TwitchPageRequest(After: "abc", PageSize: 50)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Title.Should().Be("Heads or Tails?");
        result.Value.Items[0].Choices.Should().ContainSingle().Which.Votes.Should().Be(10);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("polls");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "50");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "poll-a");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "poll-b");
    }

    [Fact]
    public async Task CreatePoll_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPollsApi api = Build(transport);

        Result<TwitchPoll> result = await api.CreatePollAsync(
            Tenant,
            new CreatePollRequest("Q?", [new CreatePollChoiceRequest("A")], 60)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreatePoll_WithScope_BuildsUserTokenPost_PutsBroadcasterIdInBody_MapsDto()
    {
        CapturingHelixTransport transport = new() { SingleResult = SamplePoll() };
        TwitchPollsApi api = Build(transport, TwitchScopes.ChannelManagePolls);
        CreatePollRequest request = new(
            "Heads or Tails?",
            [new CreatePollChoiceRequest("Heads"), new CreatePollChoiceRequest("Tails")],
            300,
            ChannelPointsVotingEnabled: true,
            ChannelPointsPerVote: 100
        );

        Result<TwitchPoll> result = await api.CreatePollAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Heads or Tails?");
        result.Value.Status.Should().Be("ACTIVE");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("polls");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().BeNull();
        transport.LastRequest.Body.Should().BeOfType<CreatePollRequest>();
        CreatePollRequest sentBody = (CreatePollRequest)transport.LastRequest.Body!;
        sentBody.BroadcasterId.Should().Be(TwitchId);
        sentBody.Title.Should().Be("Heads or Tails?");
        sentBody.Duration.Should().Be(300);
        sentBody.Choices.Should().HaveCount(2);
        sentBody.ChannelPointsPerVote.Should().Be(100);
    }

    [Fact]
    public async Task EndPoll_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchPollsApi api = Build(transport);

        Result<TwitchPoll> result = await api.EndPollAsync(Tenant, "poll-id", "TERMINATED");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EndPoll_WithScope_BuildsUserTokenPatch_PutsBroadcasterIdAndStatusInBody_MapsDto()
    {
        CapturingHelixTransport transport = new() { SingleResult = SamplePoll("TERMINATED") };
        TwitchPollsApi api = Build(transport, TwitchScopes.ChannelManagePolls);

        Result<TwitchPoll> result = await api.EndPollAsync(Tenant, "poll-id", "TERMINATED");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("TERMINATED");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("polls");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Query.Should().BeNull();
        transport.LastRequest.Body.Should().BeOfType<EndPollRequest>();
        EndPollRequest sentBody = (EndPollRequest)transport.LastRequest.Body!;
        sentBody.BroadcasterId.Should().Be(TwitchId);
        sentBody.Id.Should().Be("poll-id");
        sentBody.Status.Should().Be("TERMINATED");
    }
}
