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
/// Behavioural tests for the Streams sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates on the required scope, and builds the exact Helix request (verb / path / auth / query / body).
/// The capturing transport lets us assert the request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchStreamsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-3333-7333-8333-000000000003");
    private const string TwitchId = "44322889";

    private static TwitchStreamsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchStream Stream(string id = "123", int viewers = 7) =>
        new(
            id,
            TwitchId,
            "login",
            "Name",
            "509658",
            "Software & Game Development",
            "live",
            "Building a bot",
            ["coding"],
            viewers,
            DateTimeOffset.UnixEpoch,
            "en",
            "https://thumb",
            false
        );

    [Fact]
    public async Task GetStreams_BuildsAppTokenPagedQuery_RepeatingListFilters()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchStream>([Stream()], "cursor", 0),
        };
        TwitchStreamsApi api = Build(transport);

        Result<TwitchPage<TwitchStream>> result = await api.GetStreamsAsync(
            new TwitchStreamsFilter(
                UserIds: ["1", "2"],
                UserLogins: ["a"],
                GameIds: ["509658"],
                Languages: ["en"],
                Type: "live"
            ),
            new TwitchPageRequest(After: "abc", PageSize: 40)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        result
            .Value.Items.Should()
            .ContainSingle()
            .Which.GameName.Should()
            .Be("Software & Game Development");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("streams");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "user_id" && q.Value == "1");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "user_id" && q.Value == "2");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "user_login" && q.Value == "a");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "game_id" && q.Value == "509658");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "language" && q.Value == "en");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "type" && q.Value == "live");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "40");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetStreams_EmptyFilter_OmitsOptionalParams()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchStream>([], null, 0),
        };
        TwitchStreamsApi api = Build(transport);

        await api.GetStreamsAsync(new TwitchStreamsFilter(), new TwitchPageRequest(PageSize: 100));

        transport.LastRequest!.Query.Should().Contain(q => q.Key == "first" && q.Value == "100");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "user_id");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "type");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "after");
    }

    [Fact]
    public async Task GetStream_ResolvesTenant_BuildsAppTokenSingle_FilteredByUserId()
    {
        CapturingHelixTransport transport = new() { SingleResult = Stream(viewers: 42) };
        TwitchStreamsApi api = Build(transport);

        Result<TwitchStream> result = await api.GetStreamAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.ViewerCount.Should().Be(42);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("streams");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "user_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetStream_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchStreamsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchStream> result = await api.GetStreamAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetStreamKey_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchStreamsApi api = Build(transport); // no scopes granted

        Result<string> result = await api.GetStreamKeyAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetStreamKey_WithScope_BuildsUserTokenRequest_UnwrapsKeyString()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchStreamKey("live_44322889_secretkey"),
        };
        TwitchStreamsApi api = Build(transport, TwitchScopes.ChannelReadStreamKey);

        Result<string> result = await api.GetStreamKeyAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("live_44322889_secretkey");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("streams/key");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetFollowedStreams_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchStreamsApi api = Build(transport);

        Result<TwitchPage<TwitchStream>> result = await api.GetFollowedStreamsAsync(
            Tenant,
            new TwitchPageRequest(PageSize: 20)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetFollowedStreams_WithScope_BuildsUserTokenPagedQuery()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchStream>([Stream()], "next", 0),
        };
        TwitchStreamsApi api = Build(transport, TwitchScopes.UserReadFollows);

        Result<TwitchPage<TwitchStream>> result = await api.GetFollowedStreamsAsync(
            Tenant,
            new TwitchPageRequest(After: "cur", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("next");
        transport.LastRequest!.Path.Should().Be("streams/followed");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "cur");
    }

    [Fact]
    public async Task CreateStreamMarker_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchStreamsApi api = Build(transport);

        Result<TwitchStreamMarker> result = await api.CreateStreamMarkerAsync(Tenant, "highlight");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateStreamMarker_WithScope_BuildsUserTokenPost_WithBody_AndMapsMarker()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchStreamMarker("m1", DateTimeOffset.UnixEpoch, 244, "highlight"),
        };
        TwitchStreamsApi api = Build(transport, TwitchScopes.ChannelManageBroadcast);

        Result<TwitchStreamMarker> result = await api.CreateStreamMarkerAsync(Tenant, "highlight");

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("m1");
        result.Value.PositionSeconds.Should().Be(244);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("streams/markers");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().BeOfType<CreateStreamMarkerRequest>();
        CreateStreamMarkerRequest body = (CreateStreamMarkerRequest)transport.LastRequest.Body!;
        body.UserId.Should().Be(TwitchId);
        body.Description.Should().Be("highlight");
    }

    [Fact]
    public async Task GetStreamMarkers_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchStreamsApi api = Build(transport);

        Result<TwitchPage<TwitchStreamMarkerGroup>> result = await api.GetStreamMarkersAsync(
            Tenant,
            null,
            new TwitchPageRequest(PageSize: 20)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetStreamMarkers_WithScope_DefaultsToUserId_MapsNestedGroups()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchStreamMarkerGroup>(
                [
                    new TwitchStreamMarkerGroup(
                        TwitchId,
                        "Name",
                        "login",
                        [
                            new TwitchMarkedVideo(
                                "v1",
                                [
                                    new TwitchVideoMarker(
                                        "m1",
                                        DateTimeOffset.UnixEpoch,
                                        "clip it",
                                        300,
                                        "https://vod"
                                    ),
                                ]
                            ),
                        ]
                    ),
                ],
                null,
                0
            ),
        };
        TwitchStreamsApi api = Build(transport, TwitchScopes.UserReadBroadcast);

        Result<TwitchPage<TwitchStreamMarkerGroup>> result = await api.GetStreamMarkersAsync(
            Tenant,
            null,
            new TwitchPageRequest(PageSize: 10)
        );

        result.IsSuccess.Should().BeTrue();
        TwitchStreamMarkerGroup group = result.Value.Items.Should().ContainSingle().Subject;
        group.Videos.Should().ContainSingle().Which.Markers.Should().ContainSingle();
        group.Videos[0].Markers[0].Url.Should().Be("https://vod");
        group.Videos[0].Markers[0].PositionSeconds.Should().Be(300);
        transport.LastRequest!.Path.Should().Be("streams/markers");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "user_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "video_id");
    }

    [Fact]
    public async Task GetStreamMarkers_WithVideoId_FiltersByVideo_OmitsUserId()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchStreamMarkerGroup>([], "cursor", 0),
        };
        TwitchStreamsApi api = Build(transport, TwitchScopes.UserReadBroadcast);

        await api.GetStreamMarkersAsync(
            Tenant,
            "987",
            new TwitchPageRequest(After: "p", PageSize: 5)
        );

        transport.LastRequest!.Query.Should().Contain(q => q.Key == "video_id" && q.Value == "987");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "user_id");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "5");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "p");
    }
}
