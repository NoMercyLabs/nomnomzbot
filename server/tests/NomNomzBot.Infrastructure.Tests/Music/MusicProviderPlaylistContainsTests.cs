// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <see cref="IMusicProviderManageApi.IsTrackInPlaylistAsync"/> reads EVERY page of a playlist
/// (Spotify follows <c>next</c>, YouTube follows <c>nextPageToken</c>) and compares track identity, and
/// that the gating front refuses a provider without the Playlists capability before any request.
/// </summary>
public sealed class MusicProviderPlaylistContainsTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f9001");

    private const string YouTubeBearer = "yt-access-token";

    [Fact]
    public async Task Spotify_finds_a_track_that_only_sits_on_the_second_page()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => IsSpotifyItems(r) && r.RequestUri!.Query.Contains("offset=50"),
            HttpStatusCode.OK,
            """{"next":null,"items":[{"item":{"uri":"spotify:track:wanted"}}]}"""
        );
        handler.RespondWhen(
            IsSpotifyItems,
            HttpStatusCode.OK,
            """
            {"next":"https://api.spotify.com/v1/playlists/pl1/items?offset=50&limit=50",
             "items":[{"item":{"uri":"spotify:track:other"}}]}
            """
        );

        Result<bool> result = await api.IsTrackInPlaylistAsync(
            ChannelId,
            "spotify",
            "pl1",
            "https://open.spotify.com/track/wanted?si=abc"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        handler.RequestUrls.Should().HaveCount(2);
        handler.RequestUrls[1].Should().Contain("offset=50");
    }

    [Fact]
    public async Task Spotify_says_false_when_the_track_is_on_no_page()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => IsSpotifyItems(r) && r.RequestUri!.Query.Contains("offset=50"),
            HttpStatusCode.OK,
            """{"next":null,"items":[{"item":{"uri":"spotify:track:three"}}]}"""
        );
        handler.RespondWhen(
            IsSpotifyItems,
            HttpStatusCode.OK,
            """
            {"next":"https://api.spotify.com/v1/playlists/pl1/items?offset=50&limit=50",
             "items":[{"item":{"uri":"spotify:track:one"}},{"item":{"uri":"spotify:track:two"}}]}
            """
        );

        Result<bool> result = await api.IsTrackInPlaylistAsync(
            ChannelId,
            "spotify",
            "pl1",
            "spotify:track:wanted"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        handler.RequestUrls.Should().HaveCount(2);
    }

    [Fact]
    public async Task YouTube_finds_a_video_that_only_sits_on_the_second_page()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => IsYouTubeItems(r) && r.RequestUri!.Query.Contains("pageToken=PAGE2"),
            HttpStatusCode.OK,
            """{"items":[{"id":"i3","contentDetails":{"videoId":"dQw4w9WgXcQ"}}]}"""
        );
        handler.RespondWhen(
            IsYouTubeItems,
            HttpStatusCode.OK,
            """
            {"nextPageToken":"PAGE2",
             "items":[{"id":"i1","contentDetails":{"videoId":"9bZkp7q19f0"}}]}
            """
        );

        Result<bool> result = await api.IsTrackInPlaylistAsync(
            ChannelId,
            "youtube",
            "PLabc",
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        handler.RequestUrls.Should().HaveCount(2);
        handler.RequestUrls[0].Should().Contain("part=contentDetails").And.Contain("maxResults=50");
        handler.RequestUrls[1].Should().Contain("pageToken=PAGE2");
    }

    [Fact]
    public async Task YouTube_says_false_when_the_video_is_on_no_page()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => IsYouTubeItems(r) && r.RequestUri!.Query.Contains("pageToken=PAGE2"),
            HttpStatusCode.OK,
            """{"items":[{"id":"i3","contentDetails":{"videoId":"aaaaaaaaaaa"}}]}"""
        );
        handler.RespondWhen(
            IsYouTubeItems,
            HttpStatusCode.OK,
            """
            {"nextPageToken":"PAGE2",
             "items":[{"id":"i1","contentDetails":{"videoId":"9bZkp7q19f0"}}]}
            """
        );

        Result<bool> result = await api.IsTrackInPlaylistAsync(
            ChannelId,
            "youtube",
            "PLabc",
            "dQw4w9WgXcQ"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        handler.RequestUrls.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_front_refuses_a_provider_without_the_Playlists_capability()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider, IMusicProviderManageApi>();
        provider.Provider.Returns("plain");
        provider.Capabilities.Returns(MusicProviderCapabilities.Library);
        MusicProviderManageApi api = new([provider]);

        Result<bool> result = await api.IsTrackInPlaylistAsync(
            ChannelId,
            "plain",
            "pl1",
            "track-1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        await ((IMusicProviderManageApi)provider)
            .DidNotReceiveWithAnyArgs()
            .IsTrackInPlaylistAsync(default, default!, default!, default!);
    }

    private static bool IsSpotifyItems(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        && request.RequestUri!.AbsolutePath.EndsWith(
            "/playlists/pl1/items",
            StringComparison.Ordinal
        );

    private static bool IsYouTubeItems(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        && request.Headers.Authorization is { Parameter: YouTubeBearer }
        && request.RequestUri!.AbsolutePath.EndsWith("/playlistItems", StringComparison.Ordinal);

    private static (MusicProviderManageApi Api, RecordingHttpHandler Handler) Build()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);
        vault.SeedConnectedYouTube(ChannelId, YouTubeBearer);

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );
        YouTubeMusicProvider youtube = YouTubeProviderFactory.Create(
            apiKey: "test-key",
            handler: handler,
            db: db,
            vault: vault
        );

        return (new MusicProviderManageApi([spotify, youtube]), handler);
    }
}
