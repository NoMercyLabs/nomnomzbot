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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the WIRED Spotify §3.10 manage surface through the real gating front — the calls that
/// previously failed closed with <c>CAPABILITY_UNSUPPORTED</c> now pass the flipped
/// <c>Library</c>/<c>Playlists</c> flags and hit the live-verified endpoints with exact wire shapes:
/// the unified library API (PUT/DELETE /me/library?uris=…, 40-URI chunks), POST /me/playlists,
/// PUT /playlists/{id} + re-read, /playlists/{id}/items writes, and the artist-follow
/// /me/following form. Scope/not-found failures map to <c>MISSING_SCOPE</c>/<c>NOT_FOUND</c>.
/// </summary>
public sealed class SpotifyMusicProviderManageWriteTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f6001");

    // ── Library (Save/Remove/Rate) ─────────────────────────────────────────────

    [Fact]
    public async Task Save_tracks_now_reaches_the_unified_library_endpoint()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);

        Result result = await api.SaveTracksAsync(
            ChannelId,
            "spotify",
            ["spotify:track:aaa111", "bbb222"]
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("PUT ")
                && url.Contains(
                    "/me/library?uris=spotify%3Atrack%3Aaaa111%2Cspotify%3Atrack%3Abbb222"
                )
            );
    }

    [Fact]
    public async Task Remove_saved_tracks_uses_DELETE_on_the_library_endpoint()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);

        Result result = await api.RemoveSavedTracksAsync(
            ChannelId,
            "spotify",
            ["spotify:track:aaa111"]
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("DELETE ")
                && url.Contains("/me/library?uris=spotify%3Atrack%3Aaaa111")
            );
    }

    [Fact]
    public async Task Library_writes_chunk_at_the_40_uri_cap()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);
        List<string> uris = Enumerable.Range(0, 85).Select(i => $"spotify:track:t{i}").ToList();

        Result result = await api.SaveTracksAsync(ChannelId, "spotify", uris);

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .HaveCount(3, "85 URIs = 40 + 40 + 5 under the live 40-URI cap");
    }

    [Theory]
    [InlineData(MusicRating.Like, "PUT ")]
    [InlineData(MusicRating.None, "DELETE ")]
    public async Task Rating_maps_to_the_library_save_and_remove_wires(
        MusicRating rating,
        string expectedVerbPrefix
    )
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);

        Result result = await api.RateTrackAsync(
            ChannelId,
            "spotify",
            "spotify:track:aaa111",
            rating
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith(expectedVerbPrefix) && url.Contains("/me/library?uris=")
            );
    }

    [Fact]
    public async Task Dislike_stays_CAPABILITY_UNSUPPORTED_on_Spotify_without_an_API_call()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();

        Result result = await api.RateTrackAsync(
            ChannelId,
            "spotify",
            "spotify:track:aaa111",
            MusicRating.Dislike
        );

        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler.RequestUrls.Should().BeEmpty();
    }

    // ── Playlists (Create/Update/Delete/Items) ─────────────────────────────────

    [Fact]
    public async Task Create_playlist_posts_to_me_playlists_and_maps_the_created_object()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith("/me/playlists", StringComparison.Ordinal),
            HttpStatusCode.Created,
            """
            {"id":"plNew","name":"Stream Bangers","uri":"spotify:playlist:plNew",
             "description":"Hype tracks","public":false,
             "images":[{"url":"https://i.scdn.co/image/plNew.jpg"}],"items":{"total":0}}
            """
        );

        Result<MusicPlaylistDto> result = await api.CreatePlaylistAsync(
            ChannelId,
            "spotify",
            new CreateMusicPlaylistDto
            {
                Name = "Stream Bangers",
                Description = "Hype tracks",
                IsPublic = false,
            }
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Should()
            .Be(
                new MusicPlaylistDto(
                    "plNew",
                    "Stream Bangers",
                    "Hype tracks",
                    false,
                    0,
                    "https://i.scdn.co/image/plNew.jpg",
                    "spotify"
                )
            );

        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"name\":\"Stream Bangers\"");
        body.Should().Contain("\"public\":false");
        body.Should().Contain("\"description\":\"Hype tracks\"");
    }

    [Fact]
    public async Task Update_playlist_puts_only_provided_fields_then_rereads_the_playlist()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Put
                && r.RequestUri!.AbsolutePath.EndsWith("/playlists/pl1", StringComparison.Ordinal),
            HttpStatusCode.OK
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/playlists/pl1", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"id":"pl1","name":"Renamed","uri":"spotify:playlist:pl1","description":null,
             "public":true,"images":[],"items":{"total":9}}
            """
        );

        Result<MusicPlaylistDto> result = await api.UpdatePlaylistAsync(
            ChannelId,
            "spotify",
            "pl1",
            new UpdateMusicPlaylistDto { Name = "Renamed" }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Renamed");
        result.Value.TrackCount.Should().Be(9, "the re-read supplies the updated shape");

        string putBody = handler.RequestBodies[0];
        putBody.Should().Contain("\"name\":\"Renamed\"");
        putBody.Should().NotContain("\"public\"", "unset fields must not be patched");
        putBody.Should().NotContain("\"description\"");
    }

    [Fact]
    public async Task Update_playlist_with_an_empty_patch_fails_validation_without_an_API_call()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();

        Result<MusicPlaylistDto> result = await api.UpdatePlaylistAsync(
            ChannelId,
            "spotify",
            "pl1",
            new UpdateMusicPlaylistDto()
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_playlist_removes_the_playlist_uri_from_the_library()
    {
        // Spotify has no hard delete — §3.10 semantics = unfollow own playlist, whose live
        // replacement is a library removal of the playlist URI.
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);

        Result result = await api.DeletePlaylistAsync(ChannelId, "spotify", "pl1");

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("DELETE ")
                && url.Contains("/me/library?uris=spotify%3Aplaylist%3Apl1")
            );
    }

    [Fact]
    public async Task Add_playlist_tracks_posts_uris_to_the_items_endpoint()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/playlists/pl1/items",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.Created,
            """{"snapshot_id":"snap1"}"""
        );

        Result result = await api.AddPlaylistTracksAsync(
            ChannelId,
            "spotify",
            "pl1",
            ["spotify:track:aaa111", "bbb222"]
        );

        result.IsSuccess.Should().BeTrue();
        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"uris\":[\"spotify:track:aaa111\",\"spotify:track:bbb222\"]");
    }

    [Fact]
    public async Task Remove_playlist_tracks_deletes_uri_objects_from_the_items_endpoint()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Delete
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/playlists/pl1/items",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.OK,
            """{"snapshot_id":"snap2"}"""
        );

        Result result = await api.RemovePlaylistTracksAsync(
            ChannelId,
            "spotify",
            "pl1",
            ["spotify:track:aaa111"]
        );

        result.IsSuccess.Should().BeTrue();
        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"items\":[{\"uri\":\"spotify:track:aaa111\"}]");
    }

    // ── Follow / unfollow ──────────────────────────────────────────────────────

    [Fact]
    public async Task Artist_follow_rides_the_documented_me_following_form()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/following", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

        Result follow = await api.FollowAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Artist,
            "art1"
        );
        Result unfollow = await api.UnfollowAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Artist,
            "spotify:artist:art1"
        );

        follow.IsSuccess.Should().BeTrue();
        unfollow.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.StartsWith("PUT ") && url.Contains("/me/following?type=artist&ids=art1")
            );
        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.StartsWith("DELETE ") && url.Contains("/me/following?type=artist&ids=art1")
            );
    }

    [Fact]
    public async Task Playlist_follow_rides_the_library_replacement_endpoint()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.OK);

        Result result = await api.FollowAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Playlist,
            "pl9"
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("PUT ") && url.Contains("/me/library?uris=spotify%3Aplaylist%3Apl9")
            );
    }

    // ── Failure mapping ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_403_on_a_manage_write_maps_to_MISSING_SCOPE()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        handler.RespondWhen(IsLibrary, HttpStatusCode.Forbidden);

        Result result = await api.SaveTracksAsync(ChannelId, "spotify", ["spotify:track:aaa111"]);

        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task A_404_on_a_playlist_write_maps_to_NOT_FOUND()
    {
        (MusicProviderManageApi api, RecordingSpotifyHandler handler) = Build();
        // No route registered — the handler answers 404, Spotify's real "no such playlist".

        Result result = await api.AddPlaylistTracksAsync(
            ChannelId,
            "spotify",
            "plMissing",
            ["spotify:track:aaa111"]
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static bool IsLibrary(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/me/library", StringComparison.Ordinal);

    private static (MusicProviderManageApi Api, RecordingSpotifyHandler Handler) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Services.Add(
            new Service
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        RecordingSpotifyHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );
        YouTubeMusicProvider youtube = new(NullLogger<YouTubeMusicProvider>.Instance);

        MusicProviderManageApi api = new([spotify, youtube]);
        return (api, handler);
    }
}
