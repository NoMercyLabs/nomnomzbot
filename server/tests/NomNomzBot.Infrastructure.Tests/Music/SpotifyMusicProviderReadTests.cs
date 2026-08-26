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

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the Spotify §3.10 library READS (added 2026-07-05) through the real gating front: GET /me/tracks
/// (saved tracks, limit/offset threaded), GET /me/tracks/contains (positional saved-check on BARE ids),
/// GET /me/following?type=artist (followed artists → <see cref="MusicFollowDto"/>), and the followed-
/// playlists fallback over GET /me/playlists (Spotify has no dedicated followed-playlists endpoint). A
/// Channel-target follow list gates on <c>Subscriptions</c> (absent for Spotify) → <c>CAPABILITY_UNSUPPORTED</c>;
/// an unconnected provider fails <c>MISSING_SCOPE</c>. Uses the REAL provider over stubbed HTTP.
/// </summary>
public sealed class SpotifyMusicProviderReadTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f8001");

    [Fact]
    public async Task Get_saved_tracks_maps_me_tracks_items_and_threads_limit_and_offset()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/tracks", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"items":[
              {"added_at":"2026-01-01T00:00:00Z","track":{"id":"t1","name":"Song One",
               "uri":"spotify:track:t1","duration_ms":210000,"explicit":true,
               "artists":[{"name":"Artist A"}],"album":{"name":"Album A",
               "images":[{"url":"https://i.scdn.co/a.jpg"}]}}}
            ]}
            """
        );

        Result<IReadOnlyList<TrackInfo>> result = await api.GetSavedTracksAsync(
            ChannelId,
            "spotify",
            limit: 25,
            offset: 10
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        TrackInfo track = result.Value[0];
        track.TrackName.Should().Be("Song One");
        track.Artist.Should().Be("Artist A");
        track.Album.Should().Be("Album A");
        track.TrackUri.Should().Be("spotify:track:t1");
        track.ProviderTrackId.Should().Be("t1");
        track.IsExplicit.Should().BeTrue();
        track.AlbumArtUrl.Should().Be("https://i.scdn.co/a.jpg");
        track.DurationMs.Should().Be(210_000);
        track.Provider.Should().Be("spotify");

        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/me/tracks?limit=25&offset=10"));
    }

    [Fact]
    public async Task Are_tracks_saved_hits_the_contains_endpoint_positionally_on_bare_ids()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/tracks/contains",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.OK,
            "[true,false]"
        );

        Result<IReadOnlyList<bool>> result = await api.AreTracksSavedAsync(
            ChannelId,
            "spotify",
            ["spotify:track:t1", "t2"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(true, false);
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/me/tracks/contains?ids=t1%2Ct2"));
    }

    [Fact]
    public async Task Are_tracks_saved_chunks_at_the_50_id_cap()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/tracks/contains",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.OK,
            "[]"
        );
        List<string> ids = [.. Enumerable.Range(0, 120).Select(i => $"track{i:D3}xxxx")];

        Result<IReadOnlyList<bool>> result = await api.AreTracksSavedAsync(
            ChannelId,
            "spotify",
            ids
        );

        result.IsSuccess.Should().BeTrue();
        handler
            .RequestUrls.Should()
            .HaveCount(3, "120 ids = 50 + 50 + 20 under the live 50-id contains cap");
    }

    [Fact]
    public async Task Are_tracks_saved_with_no_ids_returns_empty_without_a_call()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result<IReadOnlyList<bool>> result = await api.AreTracksSavedAsync(
            ChannelId,
            "spotify",
            []
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_followed_artists_maps_the_artists_wrapper_field_by_field()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/following", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"artists":{"items":[
              {"id":"art1","name":"Artist One","images":[{"url":"https://i.scdn.co/art1.jpg"}]},
              {"id":"art2","name":"Artist Two","images":[]}
            ]}}
            """
        );

        Result<IReadOnlyList<MusicFollowDto>> result = await api.GetFollowedAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Artist
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result
            .Value[0]
            .Should()
            .Be(new MusicFollowDto("art1", "Artist One", "https://i.scdn.co/art1.jpg"));
        result.Value[1].Should().Be(new MusicFollowDto("art2", "Artist Two", null));
        handler
            .RequestUrls.Should()
            .ContainSingle(url => url.Contains("/me/following?type=artist&limit="));
    }

    [Fact]
    public async Task Get_followed_playlists_reads_me_playlists_as_the_followed_list()
    {
        // Spotify exposes no dedicated followed-playlists endpoint; the playlist-target follow list reads
        // from GET /me/playlists (owned + followed).
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/playlists", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"pl1","name":"Followed Playlist","uri":"spotify:playlist:pl1",
               "images":[{"url":"https://i.scdn.co/pl1.jpg"}],"tracks":{"total":5}}
            ]}
            """
        );

        Result<IReadOnlyList<MusicFollowDto>> result = await api.GetFollowedAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Playlist
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result
            .Value[0]
            .Should()
            .Be(new MusicFollowDto("pl1", "Followed Playlist", "https://i.scdn.co/pl1.jpg"));
    }

    [Fact]
    public async Task Get_followed_channel_is_CAPABILITY_UNSUPPORTED_on_Spotify_without_a_call()
    {
        // Channel follows gate on Subscriptions, which Spotify does not declare — fails at the front.
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();

        Result<IReadOnlyList<MusicFollowDto>> result = await api.GetFollowedAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Channel
        );

        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_without_a_connection_fail_with_MISSING_SCOPE()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build(connectSpotify: false);

        Result<IReadOnlyList<TrackInfo>> saved = await api.GetSavedTracksAsync(
            ChannelId,
            "spotify"
        );
        Result<IReadOnlyList<bool>> contains = await api.AreTracksSavedAsync(
            ChannelId,
            "spotify",
            ["spotify:track:t1"]
        );
        Result<IReadOnlyList<MusicFollowDto>> followed = await api.GetFollowedAsync(
            ChannelId,
            "spotify",
            MusicFollowTarget.Artist
        );

        saved.ErrorCode.Should().Be("MISSING_SCOPE");
        contains.ErrorCode.Should().Be("MISSING_SCOPE");
        followed.ErrorCode.Should().Be("MISSING_SCOPE");
        handler.RequestUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_403_on_a_read_maps_to_MISSING_SCOPE()
    {
        (MusicProviderManageApi api, RecordingHttpHandler handler) = Build();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/me/tracks", StringComparison.Ordinal),
            HttpStatusCode.Forbidden
        );

        Result<IReadOnlyList<TrackInfo>> result = await api.GetSavedTracksAsync(
            ChannelId,
            "spotify"
        );

        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task Now_playing_reads_me_player_and_surfaces_the_real_shuffle_and_repeat_state()
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":42000,"shuffle_state":true,"repeat_state":"context",
             "item":{"id":"t9","name":"Nightcall","uri":"spotify:track:t9","duration_ms":250000,
              "explicit":false,"artists":[{"name":"Kavinsky"}],
              "album":{"name":"OutRun","images":[{"url":"https://i.scdn.co/n.jpg"}]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().NotBeNull();
        track.TrackName.Should().Be("Nightcall");
        track.IsPlaying.Should().BeTrue();
        track.ProgressMs.Should().Be(42_000);
        // The real player toggles — the dashboard now shows the actual state instead of guessing "on".
        track.ShuffleEnabled.Should().BeTrue();
        track.RepeatMode.Should().Be(MusicRepeatMode.Context);
        // Read the full playback-state endpoint (which carries shuffle/repeat), not /currently-playing.
        handler.RequestUrls.Should().Contain(url => url.Contains("/me/player"));
        handler.RequestUrls.Should().NotContain(url => url.Contains("/currently-playing"));
    }

    [Fact]
    public async Task Now_playing_surfaces_the_active_devices_real_volume()
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":0,"shuffle_state":false,"repeat_state":"off",
             "device":{"volume_percent":37},
             "item":{"id":"t1","name":"Song","uri":"spotify:track:t1","duration_ms":180000,
              "explicit":false,"artists":[{"name":"A"}],"album":{"name":"Al","images":[]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        // Not the old hardcoded 100 — the device's real reported volume, so a mute button can show and
        // trust the actual state instead of a fake "always full" number.
        track.Should().NotBeNull();
        track.VolumePercent.Should().Be(37);
    }

    [Fact]
    public async Task Now_playing_repeat_off_maps_to_Off_and_shuffle_false()
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":0,"shuffle_state":false,"repeat_state":"off",
             "item":{"id":"t1","name":"Song","uri":"spotify:track:t1","duration_ms":180000,
              "explicit":false,"artists":[{"name":"A"}],"album":{"name":"Al","images":[]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().NotBeNull();
        track.ShuffleEnabled.Should().BeFalse();
        track.RepeatMode.Should().Be(MusicRepeatMode.Off);
    }

    [Fact]
    public async Task Now_playing_surfaces_disallowed_actions_as_false_permissions()
    {
        // A real ad-break/restricted-market shape: shuffle+repeat blocked, everything else open.
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":0,"shuffle_state":false,"repeat_state":"off",
             "actions":{"disallows":{"toggling_shuffle":true,"toggling_repeat_track":true,
              "toggling_repeat_context":true,"skipping_next":true}},
             "item":{"id":"t1","name":"Song","uri":"spotify:track:t1","duration_ms":180000,
              "explicit":false,"artists":[{"name":"A"}],"album":{"name":"Al","images":[]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().NotBeNull();
        track.CanSetShuffle.Should().BeFalse();
        track.CanSetRepeat.Should().BeFalse();
        track.CanSkipNext.Should().BeFalse();
        // Not disallowed by Spotify — must stay permitted rather than defaulting to blocked.
        track.CanSkipPrevious.Should().BeTrue();
        track.CanSeek.Should().BeTrue();
        track.CanPause.Should().BeTrue();
        track.CanResume.Should().BeTrue();
    }

    [Fact]
    public async Task Now_playing_repeat_stays_settable_when_only_one_repeat_target_is_disallowed()
    {
        // Cycling can still reach the still-open target, so the single toggle stays usable.
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":0,"shuffle_state":false,"repeat_state":"off",
             "actions":{"disallows":{"toggling_repeat_track":true}},
             "item":{"id":"t1","name":"Song","uri":"spotify:track:t1","duration_ms":180000,
              "explicit":false,"artists":[{"name":"A"}],"album":{"name":"Al","images":[]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().NotBeNull();
        track.CanSetRepeat.Should().BeTrue();
    }

    [Fact]
    public async Task Now_playing_with_no_actions_object_defaults_every_permission_to_true()
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.OK,
            """
            {"is_playing":true,"progress_ms":0,"shuffle_state":false,"repeat_state":"off",
             "item":{"id":"t1","name":"Song","uri":"spotify:track:t1","duration_ms":180000,
              "explicit":false,"artists":[{"name":"A"}],"album":{"name":"Al","images":[]}}}
            """
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().NotBeNull();
        track.CanSetShuffle.Should().BeTrue();
        track.CanSetRepeat.Should().BeTrue();
        track.CanSkipNext.Should().BeTrue();
        track.CanSkipPrevious.Should().BeTrue();
        track.CanSeek.Should().BeTrue();
        track.CanPause.Should().BeTrue();
        track.CanResume.Should().BeTrue();
    }

    [Fact]
    public async Task Embedded_playback_token_is_null_when_the_streaming_scope_was_not_granted()
    {
        // A real pre-feature Spotify connection: playback scopes granted, "streaming" never requested.
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(
            ChannelId,
            scopes: ["user-read-playback-state", "user-modify-playback-state"]
        );
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(new RecordingHttpHandler()),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        string? token = await spotify.GetEmbeddedPlaybackTokenAsync(ChannelId);

        token.Should().BeNull();
    }

    [Fact]
    public async Task Embedded_playback_token_is_null_when_not_connected_at_all()
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler _) = BuildProvider(
            connectSpotify: false
        );

        string? token = await spotify.GetEmbeddedPlaybackTokenAsync(ChannelId);

        token.Should().BeNull();
    }

    [Fact]
    public async Task Embedded_playback_token_returns_the_live_access_token_when_streaming_is_granted()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(
            ChannelId,
            scopes: ["user-read-playback-state", "streaming", "user-read-email"]
        );
        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        string? token = await spotify.GetEmbeddedPlaybackTokenAsync(ChannelId);

        token.Should().Be("test-access-token");
    }

    [Fact]
    public async Task Now_playing_is_null_when_no_active_device_204()
    {
        // GET /me/player returns 204 when playback is on no device — nothing is playing, so now-playing is null.
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider();
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Get
                && r.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal),
            HttpStatusCode.NoContent
        );

        TrackInfo? track = await spotify.GetCurrentTrackAsync(ChannelId);

        track.Should().BeNull();
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (MusicProviderManageApi Api, RecordingHttpHandler Handler) Build(
        bool connectSpotify = true
    )
    {
        (SpotifyMusicProvider spotify, RecordingHttpHandler handler) = BuildProvider(
            connectSpotify
        );
        return (new([spotify]), handler);
    }

    private static (SpotifyMusicProvider Provider, RecordingHttpHandler Handler) BuildProvider(
        bool connectSpotify = true
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        FakeIntegrationTokenVault vault = new(db);
        if (connectSpotify)
            vault.SeedConnectedSpotify(ChannelId);

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        return (spotify, handler);
    }
}
