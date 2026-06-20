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
/// Behavioural tests for the Videos sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates mutations on the required scope, and builds the exact Helix request (verb / path / auth / query).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchVideosApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchVideosApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchVideo SampleVideo(string id) =>
        new(
            id,
            "9876",
            TwitchId,
            "login",
            "Name",
            "Title",
            "Description",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "https://twitch.tv/videos/" + id,
            "https://thumb",
            "public",
            42,
            "en",
            "archive",
            "3h8m33s",
            [new TwitchVideoMutedSegment(30, 120)]
        );

    [Fact]
    public async Task GetVideosByBroadcaster_ResolvesTenant_BuildsAppTokenPagedQuery_MapsDto()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchVideo>([SampleVideo("v1")], "cursor", 1),
        };
        TwitchVideosApi api = Build(transport);

        Result<TwitchPage<TwitchVideo>> result = await api.GetVideosByBroadcasterAsync(
            Tenant,
            type: "archive",
            period: "month",
            sort: "time",
            new TwitchPageRequest(After: "abc", PageSize: 20)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        TwitchVideo video = result.Value.Items.Should().ContainSingle().Subject;
        video.Id.Should().Be("v1");
        video.Duration.Should().Be("3h8m33s");
        video.Viewable.Should().Be("public");
        video.MutedSegments.Should().ContainSingle().Which.Offset.Should().Be(120);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("videos");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "20");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "type" && q.Value == "archive");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "period" && q.Value == "month");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "sort" && q.Value == "time");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetVideosByBroadcaster_OmitsOptionalFilters_WhenNull()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchVideo>([], null, 0),
        };
        TwitchVideosApi api = Build(transport);

        await api.GetVideosByBroadcasterAsync(
            Tenant,
            type: null,
            period: null,
            sort: null,
            new TwitchPageRequest(PageSize: 100)
        );

        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "type");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "period");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "sort");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "after");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetVideosByBroadcaster_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchVideosApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchPage<TwitchVideo>> result = await api.GetVideosByBroadcasterAsync(
            Tenant,
            type: null,
            period: null,
            sort: null,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetVideosByIds_BuildsRepeatedIdQuery_AppToken_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = (IReadOnlyList<TwitchVideo>)[SampleVideo("v1"), SampleVideo("v2")],
        };
        TwitchVideosApi api = Build(transport);

        Result<IReadOnlyList<TwitchVideo>> result = await api.GetVideosByIdsAsync(["v1", "v2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("videos");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().HaveCount(2);
        transport.LastRequest.Query.Should().OnlyContain(q => q.Key == "id");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "v1");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "v2");
    }

    [Fact]
    public async Task DeleteVideos_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchVideosApi api = Build(transport); // no scopes granted

        Result<IReadOnlyList<string>> result = await api.DeleteVideosAsync(Tenant, ["v1"]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteVideos_WithScope_BuildsUserTokenDelete_WithRepeatedIds_ReturnsDeletedIds()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = (IReadOnlyList<string>)["v1", "v2"],
        };
        TwitchVideosApi api = Build(transport, TwitchScopes.ChannelManageVideos);

        Result<IReadOnlyList<string>> result = await api.DeleteVideosAsync(Tenant, ["v1", "v2"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("v1", "v2");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("videos");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Query.Should().HaveCount(2);
        transport.LastRequest.Query.Should().OnlyContain(q => q.Key == "id");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "v1");
        transport.LastRequest.Query.Should().Contain(q => q.Value == "v2");
    }
}
