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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the WIRED YouTube §3.10 manage surface through the real <see cref="MusicProviderManageApi"/>
/// gating front — the calls that previously failed closed with <c>CAPABILITY_UNSUPPORTED</c> (YouTube had
/// no <c>Library</c>/<c>Playlists</c>/<c>Subscriptions</c> flag) now pass the flipped flags and hit the
/// live-verified Data API v3 endpoints with exact wire shapes: videos.rate (like/dislike/none),
/// videos.getRating, videos.list?myRating=like, playlists insert/update(read-merge-PUT)/delete/list,
/// playlistItems insert + list→delete, subscriptions insert + list→delete + list. Every call rides the
/// broadcaster's own <c>youtube.manage</c> OAuth bearer from the vault (NOT the app key); an unconnected
/// channel fails <c>MISSING_SCOPE</c>, a 401/403 maps to <c>MISSING_SCOPE</c>, a 404 to <c>NOT_FOUND</c>.
/// Uses the REAL provider over a stubbed HTTP transport through the real front, so the tests prove
/// production wiring, not substitutes.
/// </summary>
public sealed class YouTubeMusicProviderManageTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f7001");

    private const string BearerToken = "yt-access-token";
    private const string VideoId = "dQw4w9WgXcQ";
    private const string OtherVideoId = "9bZkp7q19f0";

    // ── Library: rate / save / remove ──────────────────────────────────────────

    [Theory]
    [InlineData(MusicRating.Like, "rating=like")]
    [InlineData(MusicRating.Dislike, "rating=dislike")]
    [InlineData(MusicRating.None, "rating=none")]
    public async Task Rate_track_posts_to_videos_rate_with_the_mapped_rating_and_the_vault_bearer(
        MusicRating rating,
        string expectedRatingQuery
    )
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Post && IsBearer(r) && IsPath(r, "/videos/rate"),
            HttpStatusCode.NoContent
        );

        Result result = await api.RateTrackAsync(ChannelId, "youtube", VideoId, rating);

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("POST ")
                && url.Contains($"/videos/rate?id={VideoId}")
                && url.Contains(expectedRatingQuery)
            );
    }

    [Fact]
    public async Task Rate_track_with_an_unparseable_video_fails_validation_without_a_call()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result result = await api.RateTrackAsync(
            ChannelId,
            "youtube",
            "not a video",
            MusicRating.Like
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_tracks_rates_each_video_like()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(r => IsPath(r, "/videos/rate"), HttpStatusCode.NoContent);

        Result result = await api.SaveTracksAsync(ChannelId, "youtube", [VideoId, OtherVideoId]);

        result.IsSuccess.Should().BeTrue();
        handler.RequestUrls.Should().HaveCount(2);
        handler
            .RequestUrls.Should()
            .OnlyContain(url => url.Contains("/videos/rate?") && url.Contains("rating=like"));
    }

    [Fact]
    public async Task Remove_saved_tracks_rates_each_video_none()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(r => IsPath(r, "/videos/rate"), HttpStatusCode.NoContent);

        Result result = await api.RemoveSavedTracksAsync(ChannelId, "youtube", [VideoId]);

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.StartsWith("POST ")
                && url.Contains($"/videos/rate?id={VideoId}")
                && url.Contains("rating=none")
            );
    }

    [Fact]
    public async Task Rate_without_a_youtube_connection_fails_MISSING_SCOPE_without_a_call()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build(connectYouTube: false);

        Result result = await api.RateTrackAsync(ChannelId, "youtube", VideoId, MusicRating.Like);

        result.ErrorCode.Should().Be("MISSING_SCOPE");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_401_on_a_manage_write_maps_to_MISSING_SCOPE()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(r => IsPath(r, "/videos/rate"), HttpStatusCode.Unauthorized);

        Result result = await api.RateTrackAsync(ChannelId, "youtube", VideoId, MusicRating.Like);

        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    // ── Playlists: create / update / delete / list / items ─────────────────────

    [Fact]
    public async Task Create_playlist_posts_snippet_and_status_and_maps_the_created_object()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Post && IsBearer(r) && IsPath(r, "/playlists"),
            HttpStatusCode.OK,
            """
            {"id":"PLnew","snippet":{"title":"Stream Bangers","description":"Hype tracks",
             "thumbnails":{"high":{"url":"https://i.ytimg.com/plnew.jpg"}}},
             "status":{"privacyStatus":"private"},"contentDetails":{"itemCount":0}}
            """
        );

        Result<MusicPlaylistDto> result = await api.CreatePlaylistAsync(
            ChannelId,
            "youtube",
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
                    "PLnew",
                    "Stream Bangers",
                    "Hype tracks",
                    false,
                    0,
                    "https://i.ytimg.com/plnew.jpg",
                    "youtube"
                )
            );

        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"title\":\"Stream Bangers\"");
        body.Should().Contain("\"description\":\"Hype tracks\"");
        body.Should().Contain("\"privacyStatus\":\"private\"");
    }

    [Fact]
    public async Task Create_playlist_public_sets_privacyStatus_public()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Post && IsPath(r, "/playlists"),
            HttpStatusCode.OK,
            """{"id":"PLpub","snippet":{"title":"Public"},"status":{"privacyStatus":"public"},"contentDetails":{"itemCount":0}}"""
        );

        Result<MusicPlaylistDto> result = await api.CreatePlaylistAsync(
            ChannelId,
            "youtube",
            new CreateMusicPlaylistDto { Name = "Public", IsPublic = true }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPublic.Should().BeTrue();
        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"privacyStatus\":\"public\"");
    }

    [Fact]
    public async Task Update_playlist_reads_current_then_puts_merged_snippet_carrying_the_id()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        // playlists.update REPLACES the snippet — the provider reads the current one first, then PUTs a
        // merged body: renamed title, preserved description + privacy.
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/playlists"),
            HttpStatusCode.OK,
            """{"items":[{"id":"PL1","snippet":{"title":"Old","description":"Kept desc"},"status":{"privacyStatus":"private"}}]}"""
        );
        handler.RespondWhen(
            r => r.Method == HttpMethod.Put && IsPath(r, "/playlists"),
            HttpStatusCode.OK,
            """{"id":"PL1","snippet":{"title":"Renamed","description":"Kept desc"},"status":{"privacyStatus":"private"},"contentDetails":{"itemCount":9}}"""
        );

        Result<MusicPlaylistDto> result = await api.UpdatePlaylistAsync(
            ChannelId,
            "youtube",
            "PL1",
            new UpdateMusicPlaylistDto { Name = "Renamed" }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Renamed");
        result.Value.TrackCount.Should().Be(9, "the PUT response supplies the updated shape");

        string putBody = handler.RequestBodies.Single(b => b.Length > 0);
        putBody.Should().Contain("\"id\":\"PL1\"", "playlists.update needs the id in the body");
        putBody.Should().Contain("\"title\":\"Renamed\"");
        putBody
            .Should()
            .Contain("\"description\":\"Kept desc\"", "an unset field keeps the current value");
        putBody.Should().Contain("\"privacyStatus\":\"private\"");
    }

    [Fact]
    public async Task Update_playlist_with_an_empty_patch_fails_validation_without_a_call()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result<MusicPlaylistDto> result = await api.UpdatePlaylistAsync(
            ChannelId,
            "youtube",
            "PL1",
            new UpdateMusicPlaylistDto()
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_playlist_maps_a_missing_playlist_to_NOT_FOUND()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/playlists"),
            HttpStatusCode.OK,
            """{"items":[]}"""
        );

        Result<MusicPlaylistDto> result = await api.UpdatePlaylistAsync(
            ChannelId,
            "youtube",
            "PLmissing",
            new UpdateMusicPlaylistDto { Name = "X" }
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_playlist_deletes_by_id()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Delete && IsBearer(r) && IsPath(r, "/playlists"),
            HttpStatusCode.NoContent
        );

        Result result = await api.DeletePlaylistAsync(ChannelId, "youtube", "PL1");

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.StartsWith("DELETE ") && url.Contains("/playlists?id=PL1"));
    }

    [Fact]
    public async Task List_playlists_maps_the_provider_payload_field_by_field()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && IsBearer(r)
                && IsPath(r, "/playlists")
                && r.RequestUri!.Query.Contains("mine=true"),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"PL1","snippet":{"title":"Bangers","description":"Hype",
               "thumbnails":{"high":{"url":"https://i.ytimg.com/pl1.jpg"}}},
               "status":{"privacyStatus":"public"},"contentDetails":{"itemCount":12}},
              {"id":"PL2","snippet":{"title":"Chill","description":""},
               "status":{"privacyStatus":"private"},"contentDetails":{"itemCount":3}}
            ]}
            """
        );

        Result<IReadOnlyList<MusicPlaylistDto>> result = await api.ListPlaylistsAsync(
            ChannelId,
            "youtube"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result
            .Value[0]
            .Should()
            .Be(
                new MusicPlaylistDto(
                    "PL1",
                    "Bangers",
                    "Hype",
                    true,
                    12,
                    "https://i.ytimg.com/pl1.jpg",
                    "youtube"
                )
            );
        result.Value[1].Description.Should().BeNull("an empty description means 'none'");
        result.Value[1].IsPublic.Should().BeFalse("privacyStatus 'private' is not public");
        result.Value[1].TrackCount.Should().Be(3);
        result.Value[1].ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Add_playlist_tracks_inserts_a_playlist_item_per_video()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Post && IsBearer(r) && IsPath(r, "/playlistItems"),
            HttpStatusCode.OK,
            """{"id":"PLI1"}"""
        );

        Result result = await api.AddPlaylistTracksAsync(ChannelId, "youtube", "PL1", [VideoId]);

        result.IsSuccess.Should().BeTrue();
        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"playlistId\":\"PL1\"");
        body.Should().Contain("\"kind\":\"youtube#video\"");
        body.Should().Contain($"\"videoId\":\"{VideoId}\"");
    }

    [Fact]
    public async Task Remove_playlist_tracks_resolves_the_item_id_then_deletes_it()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        // playlistItems.delete needs the playlistItem id, not the video id — the provider lists first.
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/playlistItems"),
            HttpStatusCode.OK,
            """{"items":[{"id":"PLI-abc"}]}"""
        );
        handler.RespondWhen(
            r => r.Method == HttpMethod.Delete && IsPath(r, "/playlistItems"),
            HttpStatusCode.NoContent
        );

        Result result = await api.RemovePlaylistTracksAsync(ChannelId, "youtube", "PL1", [VideoId]);

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.StartsWith("GET ")
                && url.Contains($"/playlistItems?part=id&playlistId=PL1&videoId={VideoId}")
            );
        handler
            .RequestUrls.Should()
            .Contain(url => url.StartsWith("DELETE ") && url.Contains("/playlistItems?id=PLI-abc"));
    }

    [Fact]
    public async Task Remove_playlist_tracks_maps_a_missing_item_to_NOT_FOUND()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/playlistItems"),
            HttpStatusCode.OK,
            """{"items":[]}"""
        );

        Result result = await api.RemovePlaylistTracksAsync(ChannelId, "youtube", "PL1", [VideoId]);

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ── Subscriptions: follow / unfollow (channel) ─────────────────────────────

    [Fact]
    public async Task Follow_channel_inserts_a_subscription_with_the_channel_resource()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Post && IsBearer(r) && IsPath(r, "/subscriptions"),
            HttpStatusCode.OK,
            """{"id":"SUB1"}"""
        );

        Result result = await api.FollowAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Channel,
            "UC_channel"
        );

        result.IsSuccess.Should().BeTrue();
        string body = handler.RequestBodies.Single(b => b.Length > 0);
        body.Should().Contain("\"kind\":\"youtube#channel\"");
        body.Should().Contain("\"channelId\":\"UC_channel\"");
    }

    [Fact]
    public async Task Unfollow_channel_resolves_the_subscription_id_then_deletes_it()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/subscriptions"),
            HttpStatusCode.OK,
            """{"items":[{"id":"SUB-xyz"}]}"""
        );
        handler.RespondWhen(
            r => r.Method == HttpMethod.Delete && IsPath(r, "/subscriptions"),
            HttpStatusCode.NoContent
        );

        Result result = await api.UnfollowAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Channel,
            "UC_channel"
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .Contain(url =>
                url.StartsWith("GET ")
                && url.Contains("/subscriptions?part=id&mine=true&forChannelId=UC_channel")
            );
        handler
            .RequestUrls.Should()
            .Contain(url => url.StartsWith("DELETE ") && url.Contains("/subscriptions?id=SUB-xyz"));
    }

    [Fact]
    public async Task Unfollow_channel_with_no_subscription_maps_to_NOT_FOUND()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsPath(r, "/subscriptions"),
            HttpStatusCode.OK,
            """{"items":[]}"""
        );

        Result result = await api.UnfollowAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Channel,
            "UC_channel"
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Follow_artist_is_CAPABILITY_UNSUPPORTED_on_YouTube_without_a_call()
    {
        // YouTube has no artist-follow analogue (channels only). The front passes (Library declared),
        // the provider fails closed — never a throw, never an API call.
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result result = await api.FollowAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Artist,
            "some-artist"
        );

        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler.RequestUrls.Should().BeEmpty();
    }

    // ── Reads: saved list / contains / followed ────────────────────────────────

    [Fact]
    public async Task Get_saved_tracks_reads_the_liked_videos_over_oauth_and_maps_them()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && IsBearer(r)
                && IsPath(r, "/videos")
                && r.RequestUri!.Query.Contains("myRating=like"),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"dQw4w9WgXcQ","snippet":{"title":"Never Gonna Give You Up","channelTitle":"Rick Astley",
               "liveBroadcastContent":"none","thumbnails":{"high":{"url":"https://i.ytimg.com/rick.jpg"}}},
               "contentDetails":{"duration":"PT3M33S"},"status":{"embeddable":true}}
            ]}
            """
        );

        Result<IReadOnlyList<TrackInfo>> result = await api.GetSavedTracksAsync(
            ChannelId,
            "youtube"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        TrackInfo track = result.Value[0];
        track.TrackName.Should().Be("Never Gonna Give You Up");
        track.Artist.Should().Be("Rick Astley");
        track.ProviderTrackId.Should().Be("dQw4w9WgXcQ");
        track.TrackUri.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        track.DurationMs.Should().Be(213_000, "PT3M33S = 213s");
        track.AlbumArtUrl.Should().Be("https://i.ytimg.com/rick.jpg");
        track.IsEmbeddable.Should().BeTrue();
        track.Provider.Should().Be("youtube");
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/videos?") && !url.Contains("key="));
    }

    [Fact]
    public async Task Are_tracks_saved_maps_getRating_positionally_over_oauth()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.Method == HttpMethod.Get && IsBearer(r) && IsPath(r, "/videos/getRating"),
            HttpStatusCode.OK,
            """{"items":[{"videoId":"dQw4w9WgXcQ","rating":"like"},{"videoId":"9bZkp7q19f0","rating":"none"}]}"""
        );

        // Third id is absent from the response (never rated) → false.
        Result<IReadOnlyList<bool>> result = await api.AreTracksSavedAsync(
            ChannelId,
            "youtube",
            [VideoId, OtherVideoId, "kJQP7kiw5Fk"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(true, false, false);
        handler
            .RequestUrls.Should()
            .ContainSingle(url =>
                url.Contains("/videos/getRating?id=")
                && url.Contains(VideoId)
                && url.Contains(OtherVideoId)
                && url.Contains("kJQP7kiw5Fk")
            );
    }

    [Fact]
    public async Task Get_followed_channels_maps_subscriptions_to_follow_dtos()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && IsBearer(r)
                && IsPath(r, "/subscriptions")
                && r.RequestUri!.Query.Contains("mine=true"),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"SUB1","snippet":{"title":"LoFi Girl","resourceId":{"channelId":"UC_lofi"},
               "thumbnails":{"high":{"url":"https://i.ytimg.com/lofi.jpg"}}}}
            ]}
            """
        );

        Result<IReadOnlyList<MusicFollowDto>> result = await api.GetFollowedAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Channel
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result
            .Value[0]
            .Should()
            .Be(new MusicFollowDto("UC_lofi", "LoFi Girl", "https://i.ytimg.com/lofi.jpg"));
    }

    [Fact]
    public async Task Get_followed_artist_is_CAPABILITY_UNSUPPORTED_on_YouTube_without_a_call()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result<IReadOnlyList<MusicFollowDto>> result = await api.GetFollowedAsync(
            ChannelId,
            "youtube",
            MusicFollowTarget.Artist
        );

        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler.RequestUrls.Should().BeEmpty();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static bool IsBearer(HttpRequestMessage request) =>
        request.Headers.Authorization is { Scheme: "Bearer", Parameter: BearerToken };

    private static bool IsPath(HttpRequestMessage request, string suffix) =>
        request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal);

    private static (MusicProviderManageApi Api, RecordingHttpHandler Handler) Build(
        bool connectYouTube = true
    )
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        if (connectYouTube)
        {
            db.Services.Add(
                new Service
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "youtube",
                    BroadcasterId = ChannelId,
                    Enabled = true,
                    AccessToken = BearerToken,
                }
            );
            db.SaveChanges();
        }

        RecordingHttpHandler handler = new();
        YouTubeMusicProvider youtube = YouTubeProviderFactory.Create(
            apiKey: "test-key",
            handler: handler,
            db: db
        );

        MusicProviderManageApi api = new([youtube]);
        return (api, handler);
    }
}
