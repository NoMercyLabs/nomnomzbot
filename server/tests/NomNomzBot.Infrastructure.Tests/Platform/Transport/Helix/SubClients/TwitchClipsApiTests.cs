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
/// Behavioural tests for the Clips sub-client: each tenant-scoped method resolves the tenant Guid to the
/// Twitch id, gates on the required scope, and builds the exact Helix request (verb / path / auth / query),
/// while the id-keyed read stays a tenant-less App-token lookup. The capturing transport lets us assert the
/// request shape and the short-circuit paths with no HTTP.
/// </summary>
public class TwitchClipsApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchClipsApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchClip SampleClip(string id = "AwkwardHelplessSalamanderSwiftRage") =>
        new(
            id,
            "https://clips.twitch.tv/AwkwardHelplessSalamanderSwiftRage",
            "https://clips.twitch.tv/embed?clip=AwkwardHelplessSalamanderSwiftRage",
            TwitchId,
            "Broadcaster",
            "9876",
            "Creator",
            "video-1",
            "509658",
            "en",
            "Insane clutch",
            142,
            DateTimeOffset.UnixEpoch,
            "https://clips-media-assets.twitch.tv/preview-480x272.jpg",
            12.5,
            872,
            false
        );

    [Fact]
    public async Task CreateClip_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchClipsApi api = Build(transport); // no scopes granted

        Result<TwitchClipStub> result = await api.CreateClipAsync(Tenant, hasDelay: false);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateClip_WithScope_BuildsUserTokenPost_WithHasDelay_MapsStub()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchClipStub("clip-1", "https://clips.twitch.tv/clip-1/edit"),
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.ClipsEdit);

        Result<TwitchClipStub> result = await api.CreateClipAsync(Tenant, hasDelay: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("clip-1");
        result.Value.EditUrl.Should().Be("https://clips.twitch.tv/clip-1/edit");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("clips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "has_delay" && q.Value == "true");
    }

    [Fact]
    public async Task CreateClip_NullHasDelay_OmitsTheQueryParam()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchClipStub("clip-2", "https://clips.twitch.tv/clip-2/edit"),
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.ClipsEdit);

        Result<TwitchClipStub> result = await api.CreateClipAsync(Tenant, hasDelay: null);

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "has_delay");
    }

    [Fact]
    public async Task CreateClipFromVod_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchClipsApi api = Build(transport); // no scopes granted

        Result<TwitchClipStub> result = await api.CreateClipFromVodAsync(
            Tenant,
            new CreateClipFromVodRequest("9876", "video-1", 320, "Clutch moment")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateClipFromVod_WithScope_BuildsUserTokenPost_WithEveryFieldAsQuery()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchClipStub("clip-3", "https://clips.twitch.tv/clip-3/edit"),
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.EditorManageClips);
        CreateClipFromVodRequest request = new(
            "9876",
            "video-1",
            320,
            "Clutch moment",
            Duration: 20
        );

        Result<TwitchClipStub> result = await api.CreateClipFromVodAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("clip-3");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("videos/clips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "editor_id" && q.Value == "9876");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "vod_id" && q.Value == "video-1");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "vod_offset" && q.Value == "320");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "title" && q.Value == "Clutch moment");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "duration" && q.Value == "20");
    }

    [Fact]
    public async Task CreateClipFromVod_NullDuration_OmitsTheQueryParam()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = new TwitchClipStub("clip-4", "https://clips.twitch.tv/clip-4/edit"),
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.EditorManageClips);

        Result<TwitchClipStub> result = await api.CreateClipFromVodAsync(
            Tenant,
            new CreateClipFromVodRequest("9876", "video-1", 320, "Clutch moment")
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().NotContain(q => q.Key == "duration");
    }

    [Fact]
    public async Task GetClipsByBroadcaster_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchClipsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchPage<TwitchClip>> result = await api.GetClipsByBroadcasterAsync(
            Tenant,
            new TwitchPageRequest()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetClipsByBroadcaster_BuildsAppTokenPagedQuery_MapsPage()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchClip>([SampleClip()], "cursor", 0),
        };
        TwitchClipsApi api = Build(transport); // no scope required

        Result<TwitchPage<TwitchClip>> result = await api.GetClipsByBroadcasterAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.NextCursor.Should().Be("cursor");
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].Duration.Should().Be(12.5);
        result.Value.Items[0].VodOffset.Should().Be(872);
        result.Value.Items[0].IsFeatured.Should().BeFalse();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("clips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetClipDownloadUrls_MissingBothClipScopes_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchClipsApi api = Build(transport); // neither editor:manage:clips nor channel:manage:clips

        Result<IReadOnlyList<TwitchClipDownload>> result = await api.GetClipDownloadUrlsAsync(
            Tenant,
            TwitchId,
            ["InexpensiveDistinctFoxChefFrank"]
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetClipDownloadUrls_WithEditorScope_BuildsUserTokenGet_OneClipIdParamPerClip_MapsUrls()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult =
                (IReadOnlyList<TwitchClipDownload>)
                    [
                        new TwitchClipDownload(
                            "InexpensiveDistinctFoxChefFrank",
                            "https://production.assets.clips.twitchcdn.net/yFZG",
                            null
                        ),
                        new TwitchClipDownload(
                            "SpinelessCloudyLeopardMcaT",
                            "https://production.assets.clips.twitchcdn.net/542j",
                            "https://production.assets.clips.twitchcdn.net/542j-portrait"
                        ),
                    ],
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.EditorManageClips);

        Result<IReadOnlyList<TwitchClipDownload>> result = await api.GetClipDownloadUrlsAsync(
            Tenant,
            TwitchId,
            ["InexpensiveDistinctFoxChefFrank", "SpinelessCloudyLeopardMcaT"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].ClipId.Should().Be("InexpensiveDistinctFoxChefFrank");
        result
            .Value[0]
            .LandscapeDownloadUrl.Should()
            .Be("https://production.assets.clips.twitchcdn.net/yFZG");
        result.Value[0].PortraitDownloadUrl.Should().BeNull();
        result
            .Value[1]
            .PortraitDownloadUrl.Should()
            .Be("https://production.assets.clips.twitchcdn.net/542j-portrait");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("clips/downloads");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "editor_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "clip_id" && q.Value == "InexpensiveDistinctFoxChefFrank");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "clip_id" && q.Value == "SpinelessCloudyLeopardMcaT");
        transport.LastRequest.Query!.Count(q => q.Key == "clip_id").Should().Be(2);
    }

    [Fact]
    public async Task GetClipDownloadUrls_WithChannelManageScopeAlone_AlsoPasses()
    {
        // Twitch accepts channel:manage:clips as the broadcaster's alternative to editor:manage:clips.
        CapturingHelixTransport transport = new()
        {
            ListResult = (IReadOnlyList<TwitchClipDownload>)[],
        };
        TwitchClipsApi api = Build(transport, TwitchScopes.ChannelManageClips);

        Result<IReadOnlyList<TwitchClipDownload>> result = await api.GetClipDownloadUrlsAsync(
            Tenant,
            TwitchId,
            ["InexpensiveDistinctFoxChefFrank"]
        );

        result.IsSuccess.Should().BeTrue();
        transport.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetClipDownloadUrls_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchClipsApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver(TwitchScopes.EditorManageClips)
        );

        Result<IReadOnlyList<TwitchClipDownload>> result = await api.GetClipDownloadUrlsAsync(
            Tenant,
            TwitchId,
            ["InexpensiveDistinctFoxChefFrank"]
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetClipsByIds_BuildsAppTokenRequest_WithRepeatedIdParams()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = (IReadOnlyList<TwitchClip>)[SampleClip("a"), SampleClip("b")],
        };
        TwitchClipsApi api = Build(transport);

        Result<IReadOnlyList<TwitchClip>> result = await api.GetClipsByIdsAsync(["a", "b"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("clips");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "a");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "b");
        transport.LastRequest.Query!.Count(q => q.Key == "id").Should().Be(2);
    }
}
