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
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Spotify Web API music provider.
/// Requires the broadcaster to have connected their Spotify account (Premium required for transport —
/// a player write rejected 403/<c>PREMIUM_REQUIRED</c> throws <see cref="PremiumRequiredException"/>,
/// which the first Result-typed surface maps to <c>Failure("PREMIUM_REQUIRED")</c>, and flips the
/// observed <c>spotify.premium</c> capability for the integrations status surface).
/// Token stored as Service(Name="spotify", BroadcasterId=broadcasterId).
///
/// Live-reference notes (verified 2026-07-05):
/// - Search max 10 results per type; no batch GET /tracks?ids=; no browse endpoints
/// - Create Playlist is POST /me/playlists (the /users/{id}/playlists form is gone)
/// - Playlist item writes ride /playlists/{id}/items (the /tracks forms are deprecated)
/// - Library writes ride PUT/DELETE /me/library?uris= (replaces /me/tracks writes,
///   follow/unfollow-playlist, and follow/unfollow-user); artist follows still ride the
///   deprecated-but-documented /me/following?type=artist (its replacement takes no artist URIs)
/// - Library READS stay on the original endpoints (only the writes moved): GET /me/tracks
///   (saved tracks, scope user-library-read), GET /me/tracks/contains?ids= (positional saved-check,
///   max 50 ids), GET /me/following?type=artist (followed artists, scope user-follow-read). Spotify
///   has NO dedicated followed-playlists endpoint — GET /me/playlists returns owned + followed, so a
///   playlist-target follow list reads from there.
/// </summary>
public sealed class SpotifyMusicProvider
    : IMusicProvider,
        IMusicRemoteProvider,
        IMusicProviderManageApi
{
    private const string SpotifyApiBase = "https://api.spotify.com/v1";
    private const string SpotifyTokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ProviderName = "spotify";
    private const string PremiumCapabilityKey = "spotify.premium";
    private const int LibraryUrisPerRequest = 40; // /me/library hard cap per live reference
    private const int ContainsIdsPerRequest = 50; // GET /me/tracks/contains hard cap per live reference
    private const int SavedTracksPerPage = 50; // GET /me/tracks limit hard cap per live reference

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly IIntegrationCapabilityStore _capabilities;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SpotifyMusicProvider> _logger;

    public SpotifyMusicProvider(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        IIntegrationCapabilityStore capabilities,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<SpotifyMusicProvider> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _capabilities = capabilities;
        _http = httpClientFactory.CreateClient("spotify");
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// The full §3.5 Spotify set (music-sr.md line 324): complete remote transport plus the
    /// library/playlist manage surface. No <c>Subscriptions</c> — Spotify has no channel-follow
    /// analogue. Premium gating is a runtime signal (<c>PREMIUM_REQUIRED</c>), not a capability flag.
    /// </summary>
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue
        | MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Volume
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.Seek
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.AcceptsSongRequests
        | MusicProviderCapabilities.Previous
        | MusicProviderCapabilities.Shuffle
        | MusicProviderCapabilities.Repeat
        | MusicProviderCapabilities.TransferDevice
        | MusicProviderCapabilities.Library
        | MusicProviderCapabilities.Playlists;

    public async Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        await SendPlayerCommandAsync(
            HttpMethod.Put,
            $"{SpotifyApiBase}/me/player/play",
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        await SendPlayerCommandAsync(
            HttpMethod.Put,
            $"{SpotifyApiBase}/me/player/pause",
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        await SendPlayerCommandAsync(
            HttpMethod.Post,
            $"{SpotifyApiBase}/me/player/next",
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task PreviousAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        await SendPlayerCommandAsync(
            HttpMethod.Post,
            $"{SpotifyApiBase}/me/player/previous",
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task SetVolumeAsync(
        Guid broadcasterId,
        int volumePercent,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        string url = $"{SpotifyApiBase}/me/player/volume?volume_percent={volumePercent}";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task<TrackInfo?> GetCurrentTrackAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return null;

        // Full playback state (not /currently-playing): it also carries shuffle_state + repeat_state, so the
        // dashboard shows the REAL toggle state instead of guessing. 204 = no active device → nothing playing.
        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            $"{SpotifyApiBase}/me/player",
            token,
            cancellationToken
        );
        if (response is null || response.StatusCode == HttpStatusCode.NoContent)
            return null;

        if (!response.IsSuccessStatusCode)
            return null;

        SpotifyPlaybackState? json = await response.Content.ReadFromJsonAsync<SpotifyPlaybackState>(
            cancellationToken: cancellationToken
        );
        if (json?.Item is null)
            return null;

        return MapToTrackInfo(
            json.Item,
            json.IsPlaying,
            json.ProgressMs,
            json.ShuffleState,
            ParseRepeatState(json.RepeatState)
        );
    }

    /// <summary>Spotify repeat_state ("off" | "track" | "context") → <see cref="MusicRepeatMode"/>; unknown → Off.</summary>
    private static MusicRepeatMode ParseRepeatState(string? repeatState) =>
        repeatState switch
        {
            "track" => MusicRepeatMode.Track,
            "context" => MusicRepeatMode.Context,
            _ => MusicRepeatMode.Off,
        };

    public async Task<IReadOnlyList<TrackInfo>> SearchAsync(
        Guid broadcasterId,
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return [];

        // Feb 2026: max 10 results per type
        int limit = Math.Min(maxResults, 10);
        string url =
            $"{SpotifyApiBase}/search?q={Uri.EscapeDataString(query)}&type=track&limit={limit}";

        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            url,
            token,
            cancellationToken
        );
        if (response is null || !response.IsSuccessStatusCode)
            return [];

        SpotifySearchResponse? json =
            await response.Content.ReadFromJsonAsync<SpotifySearchResponse>(
                cancellationToken: cancellationToken
            );
        if (json?.Tracks?.Items is null)
            return [];

        return json.Tracks.Items.Where(t => t is not null).Select(t => MapToTrackInfo(t)).ToList();
    }

    public async Task<TrackInfo?> ResolveTrackAsync(
        Guid broadcasterId,
        string uriOrId,
        CancellationToken cancellationToken = default
    )
    {
        string? trackId = ExtractId(uriOrId, "track");
        if (trackId is null)
            return null;

        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return null;

        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            $"{SpotifyApiBase}/tracks/{Uri.EscapeDataString(trackId)}",
            token,
            cancellationToken
        );
        if (response is null || !response.IsSuccessStatusCode)
            return null;

        SpotifyTrack? track = await response.Content.ReadFromJsonAsync<SpotifyTrack>(
            cancellationToken: cancellationToken
        );
        return track is null ? null : MapToTrackInfo(track);
    }

    public async Task<bool> AddToQueueAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return false;

        string url = $"{SpotifyApiBase}/me/player/queue?uri={Uri.EscapeDataString(trackUri)}";
        HttpResponseMessage? response = await SendPlayerCommandAsync(
            HttpMethod.Post,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );

        return response?.IsSuccessStatusCode == true;
    }

    // ─── Transport (capability-gated members) ────────────────────────────────

    public async Task SeekAsync(
        Guid broadcasterId,
        int positionSeconds,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        long positionMs = (long)positionSeconds * 1000;
        string url = $"{SpotifyApiBase}/me/player/seek?position_ms={positionMs}";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task SetShuffleAsync(
        Guid broadcasterId,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        string url =
            $"{SpotifyApiBase}/me/player/shuffle?state={enabled.ToString().ToLowerInvariant()}";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task SetRepeatAsync(
        Guid broadcasterId,
        MusicRepeatMode mode,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        string state = mode switch
        {
            MusicRepeatMode.Track => "track",
            MusicRepeatMode.Context => "context",
            _ => "off",
        };
        string url = $"{SpotifyApiBase}/me/player/repeat?state={state}";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );
    }

    public async Task TransferPlaybackAsync(
        Guid broadcasterId,
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        string url = $"{SpotifyApiBase}/me/player";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            new { device_ids = new[] { deviceId }, play },
            broadcasterId,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return [];

        string url = $"{SpotifyApiBase}/me/player/devices";
        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            url,
            token,
            cancellationToken
        );
        if (response is null || !response.IsSuccessStatusCode)
            return [];

        SpotifyDevicesResponse? json =
            await response.Content.ReadFromJsonAsync<SpotifyDevicesResponse>(
                cancellationToken: cancellationToken
            );

        return json?.Devices?.Select(d => new MusicDeviceInfo(
                    d.Id,
                    d.Name,
                    d.Type,
                    d.IsActive,
                    d.VolumePercent
                ))
                .ToList()
                .AsReadOnly()
            ?? (IReadOnlyList<MusicDeviceInfo>)[];
    }

    // ─── IMusicRemoteProvider (residual — see interface doc) ─────────────────

    public async Task<IReadOnlyList<MusicPlaylist>> GetPlaylistsAsync(
        Guid broadcasterId,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return [];

        (HttpStatusCode? status, SpotifyPaging<SpotifyPlaylist>? page) =
            await FetchPlaylistsPageAsync(token, offset, limit, cancellationToken);
        if (status is null || page?.Items is null)
            return [];

        return page
            .Items.Select(p => new MusicPlaylist
            {
                Id = p.Id,
                Name = p.Name,
                Uri = p.Uri,
                TrackCount = p.ItemsPage?.Total ?? p.Tracks?.Total ?? 0,
                ImageUrl = p.Images?.FirstOrDefault()?.Url,
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task PlayContextAsync(
        Guid broadcasterId,
        string contextUri,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return;

        string url = $"{SpotifyApiBase}/me/player/play";
        await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            new { context_uri = contextUri },
            broadcasterId,
            cancellationToken
        );
    }

    // ─── IMusicProviderManageApi (§3.10 — Spotify's own manage surface) ──────

    public async Task<Result<IReadOnlyList<MusicPlaylistDto>>> ListPlaylistsAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<MusicPlaylistDto>>();

        (HttpStatusCode? status, SpotifyPaging<SpotifyPlaylist>? page) =
            await FetchPlaylistsPageAsync(token, offset: 0, limit: 50, cancellationToken);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<MusicPlaylistDto>>();

        if (page?.Items is null)
            return Unavailable<IReadOnlyList<MusicPlaylistDto>>();

        IReadOnlyList<MusicPlaylistDto> playlists = page
            .Items.Select(MapPlaylistDto)
            .ToList()
            .AsReadOnly();

        return Result.Success(playlists);
    }

    public async Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        CreateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<MusicPlaylistDto>();

        Dictionary<string, object?> body = new()
        {
            ["name"] = request.Name,
            ["public"] = request.IsPublic,
        };
        if (request.Description is not null)
            body["description"] = request.Description;

        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Post,
            $"{SpotifyApiBase}/me/playlists",
            token,
            body,
            cancellationToken
        );

        Result outcome = ManageOutcome(response, "The playlist");
        if (outcome.IsFailure)
            return outcome.WithValue<MusicPlaylistDto>(default!);

        SpotifyPlaylist? created = await response!.Content.ReadFromJsonAsync<SpotifyPlaylist>(
            cancellationToken: cancellationToken
        );
        if (created is null)
            return Unavailable<MusicPlaylistDto>();

        return Result.Success(MapPlaylistDto(created));
    }

    public async Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        UpdateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object?> body = new();
        if (request.Name is not null)
            body["name"] = request.Name;
        if (request.Description is not null)
            body["description"] = request.Description;
        if (request.IsPublic is not null)
            body["public"] = request.IsPublic;
        if (body.Count == 0)
            return Result.Failure<MusicPlaylistDto>("Nothing to update.", "VALIDATION_FAILED");

        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<MusicPlaylistDto>();

        string? id = ExtractId(playlistId, "playlist");
        if (id is null)
            return Result.Failure<MusicPlaylistDto>("Invalid playlist id.", "VALIDATION_FAILED");

        HttpResponseMessage? putResponse = await SendManageAsync(
            HttpMethod.Put,
            $"{SpotifyApiBase}/playlists/{Uri.EscapeDataString(id)}",
            token,
            body,
            cancellationToken
        );

        Result outcome = ManageOutcome(putResponse, "The playlist");
        if (outcome.IsFailure)
            return outcome.WithValue<MusicPlaylistDto>(default!);

        // PUT /playlists/{id} returns an empty body — re-read for the updated shape.
        HttpResponseMessage? getResponse = await SendAsync(
            HttpMethod.Get,
            $"{SpotifyApiBase}/playlists/{Uri.EscapeDataString(id)}",
            token,
            cancellationToken
        );
        if (getResponse is null || !getResponse.IsSuccessStatusCode)
            return Unavailable<MusicPlaylistDto>();

        SpotifyPlaylist? updated = await getResponse.Content.ReadFromJsonAsync<SpotifyPlaylist>(
            cancellationToken: cancellationToken
        );
        if (updated is null)
            return Unavailable<MusicPlaylistDto>();

        return Result.Success(MapPlaylistDto(updated));
    }

    public async Task<Result> DeletePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken cancellationToken = default
    )
    {
        // Spotify has no hard delete: removing the playlist from the library (the live replacement
        // for unfollow-own-playlist) is the specced §3.10 semantics.
        string? id = ExtractId(playlistId, "playlist");
        if (id is null)
            return Result.Failure("Invalid playlist id.", "VALIDATION_FAILED");

        return await SendLibraryWriteAsync(
            broadcasterId,
            HttpMethod.Delete,
            [$"spotify:playlist:{id}"],
            cancellationToken
        );
    }

    public async Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        string? id = ExtractId(playlistId, "playlist");
        if (id is null)
            return Result.Failure("Invalid playlist id.", "VALIDATION_FAILED");

        object body = new { uris = trackUris.Select(NormalizeTrackUri).ToArray() };
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Post,
            $"{SpotifyApiBase}/playlists/{Uri.EscapeDataString(id)}/items",
            token,
            body,
            cancellationToken
        );

        return ManageOutcome(response, "The playlist");
    }

    public async Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        string? id = ExtractId(playlistId, "playlist");
        if (id is null)
            return Result.Failure("Invalid playlist id.", "VALIDATION_FAILED");

        object body = new
        {
            items = trackUris.Select(u => new { uri = NormalizeTrackUri(u) }).ToArray(),
        };
        HttpResponseMessage? response = await SendManageAsync(
            HttpMethod.Delete,
            $"{SpotifyApiBase}/playlists/{Uri.EscapeDataString(id)}/items",
            token,
            body,
            cancellationToken
        );

        return ManageOutcome(response, "The playlist");
    }

    public async Task<Result> SaveTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) =>
        await SendLibraryWriteAsync(
            broadcasterId,
            HttpMethod.Put,
            trackUris.Select(NormalizeTrackUri).ToList(),
            cancellationToken
        );

    public async Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) =>
        await SendLibraryWriteAsync(
            broadcasterId,
            HttpMethod.Delete,
            trackUris.Select(NormalizeTrackUri).ToList(),
            cancellationToken
        );

    public async Task<Result> RateTrackAsync(
        Guid broadcasterId,
        string provider,
        string trackUri,
        MusicRating rating,
        CancellationToken cancellationToken = default
    ) =>
        rating switch
        {
            // §3.10: on Spotify, like/none map to save/remove; dislike has no analogue.
            MusicRating.Like => await SaveTracksAsync(
                broadcasterId,
                provider,
                [trackUri],
                cancellationToken
            ),
            MusicRating.None => await RemoveSavedTracksAsync(
                broadcasterId,
                provider,
                [trackUri],
                cancellationToken
            ),
            _ => Result.Failure("Spotify has no dislike rating.", "CAPABILITY_UNSUPPORTED"),
        };

    public async Task<Result> FollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    ) =>
        await SetFollowStateAsync(broadcasterId, target, targetId, follow: true, cancellationToken);

    public async Task<Result> UnfollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    ) =>
        await SetFollowStateAsync(
            broadcasterId,
            target,
            targetId,
            follow: false,
            cancellationToken
        );

    private async Task<Result> SetFollowStateAsync(
        Guid broadcasterId,
        MusicFollowTarget target,
        string targetId,
        bool follow,
        CancellationToken cancellationToken
    )
    {
        HttpMethod method = follow ? HttpMethod.Put : HttpMethod.Delete;

        switch (target)
        {
            case MusicFollowTarget.Artist:
            {
                // Live docs mark PUT/DELETE /me/following deprecated in favor of the /me/library
                // API — but /me/library accepts no artist URIs, so the documented /me/following
                // form remains the only artist-follow wire. Kept deliberately (graceful
                // degradation over deletion); revisit when the library API grows artist support.
                string? artistId = ExtractId(targetId, "artist");
                if (artistId is null)
                    return Result.Failure("Invalid artist id.", "VALIDATION_FAILED");

                string? token = await GetTokenAsync(broadcasterId, cancellationToken);
                if (token is null)
                    return NotConnected();

                string url =
                    $"{SpotifyApiBase}/me/following?type=artist&ids={Uri.EscapeDataString(artistId)}";
                HttpResponseMessage? response = await SendManageAsync(
                    method,
                    url,
                    token,
                    null,
                    cancellationToken
                );
                return ManageOutcome(response, "The artist");
            }

            case MusicFollowTarget.Playlist:
            {
                // Follow/unfollow-playlist are deprecated; the live replacement is the library API
                // with a playlist URI.
                string? playlistId = ExtractId(targetId, "playlist");
                if (playlistId is null)
                    return Result.Failure("Invalid playlist id.", "VALIDATION_FAILED");

                return await SendLibraryWriteAsync(
                    broadcasterId,
                    method,
                    [$"spotify:playlist:{playlistId}"],
                    cancellationToken
                );
            }

            default:
                // Channel targets gate on Subscriptions at the manage front and never reach here.
                return Result.Failure(
                    "Spotify has no channel subscriptions.",
                    "CAPABILITY_UNSUPPORTED"
                );
        }
    }

    /// <summary>PUT/DELETE /me/library?uris=… in chunks of <see cref="LibraryUrisPerRequest"/> —
    /// the live replacement for the deprecated /me/tracks writes and playlist/user follows.</summary>
    private async Task<Result> SendLibraryWriteAsync(
        Guid broadcasterId,
        HttpMethod method,
        IReadOnlyList<string> uris,
        CancellationToken cancellationToken
    )
    {
        if (uris.Count == 0)
            return Result.Failure("No items given.", "VALIDATION_FAILED");

        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected();

        for (int offset = 0; offset < uris.Count; offset += LibraryUrisPerRequest)
        {
            List<string> chunk = uris.Skip(offset).Take(LibraryUrisPerRequest).ToList();
            string url =
                $"{SpotifyApiBase}/me/library?uris={Uri.EscapeDataString(string.Join(",", chunk))}";
            HttpResponseMessage? response = await SendManageAsync(
                method,
                url,
                token,
                null,
                cancellationToken
            );

            Result outcome = ManageOutcome(response, "The item");
            if (outcome.IsFailure)
                return outcome;
        }

        return Result.Success();
    }

    // ─── IMusicProviderManageApi reads (§3.10 — added 2026-07-05) ────────────

    public async Task<Result<IReadOnlyList<TrackInfo>>> GetSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<TrackInfo>>();

        int cappedLimit = Math.Clamp(limit, 1, SavedTracksPerPage);
        int safeOffset = Math.Max(offset, 0);
        string url = $"{SpotifyApiBase}/me/tracks?limit={cappedLimit}&offset={safeOffset}";

        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            url,
            token,
            cancellationToken
        );
        if (response is null)
            return Unavailable<IReadOnlyList<TrackInfo>>();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<TrackInfo>>();
        if (!response.IsSuccessStatusCode)
            return Unavailable<IReadOnlyList<TrackInfo>>();

        SpotifyPaging<SpotifySavedTrack>? page = await response.Content.ReadFromJsonAsync<
            SpotifyPaging<SpotifySavedTrack>
        >(cancellationToken: cancellationToken);
        if (page?.Items is null)
            return Unavailable<IReadOnlyList<TrackInfo>>();

        IReadOnlyList<TrackInfo> tracks = page
            .Items.Where(item => item.Track is not null)
            .Select(item => MapToTrackInfo(item.Track!))
            .ToList()
            .AsReadOnly();

        return Result.Success(tracks);
    }

    public async Task<Result<IReadOnlyList<bool>>> AreTracksSavedAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        if (trackUris.Count == 0)
            return Result.Success<IReadOnlyList<bool>>([]);

        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<bool>>();

        // The contains endpoint takes BARE ids (not URIs), positional, max 50 per call.
        List<bool> flags = [];
        for (int offset = 0; offset < trackUris.Count; offset += ContainsIdsPerRequest)
        {
            List<string> chunk = trackUris
                .Skip(offset)
                .Take(ContainsIdsPerRequest)
                .Select(uri => ExtractId(uri, "track") ?? uri)
                .ToList();
            string url =
                $"{SpotifyApiBase}/me/tracks/contains?ids={Uri.EscapeDataString(string.Join(",", chunk))}";

            HttpResponseMessage? response = await SendAsync(
                HttpMethod.Get,
                url,
                token,
                cancellationToken
            );
            if (response is null)
                return Unavailable<IReadOnlyList<bool>>();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return MissingScope<IReadOnlyList<bool>>();
            if (!response.IsSuccessStatusCode)
                return Unavailable<IReadOnlyList<bool>>();

            List<bool>? chunkFlags = await response.Content.ReadFromJsonAsync<List<bool>>(
                cancellationToken: cancellationToken
            );
            if (chunkFlags is null)
                return Unavailable<IReadOnlyList<bool>>();

            flags.AddRange(chunkFlags);
        }

        return Result.Success<IReadOnlyList<bool>>(flags.AsReadOnly());
    }

    public async Task<Result<IReadOnlyList<MusicFollowDto>>> GetFollowedAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        // Channel-follow lists gate on Subscriptions (absent for Spotify) at the front and never
        // reach here; a Channel target arriving here fails closed defensively.
        if (target == MusicFollowTarget.Channel)
            return Result.Failure<IReadOnlyList<MusicFollowDto>>(
                "Spotify has no channel subscriptions.",
                "CAPABILITY_UNSUPPORTED"
            );

        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return NotConnected<IReadOnlyList<MusicFollowDto>>();

        int cappedLimit = Math.Clamp(limit, 1, 50);

        // Spotify has no dedicated followed-playlists endpoint; GET /me/playlists returns owned +
        // followed, so a playlist-target follow list reads from there.
        if (target == MusicFollowTarget.Playlist)
        {
            (HttpStatusCode? status, SpotifyPaging<SpotifyPlaylist>? page) =
                await FetchPlaylistsPageAsync(token, 0, cappedLimit, cancellationToken);
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return MissingScope<IReadOnlyList<MusicFollowDto>>();
            if (page?.Items is null)
                return Unavailable<IReadOnlyList<MusicFollowDto>>();

            IReadOnlyList<MusicFollowDto> playlists = page
                .Items.Select(p => new MusicFollowDto(
                    p.Id,
                    p.Name,
                    p.Images?.FirstOrDefault()?.Url
                ))
                .ToList()
                .AsReadOnly();
            return Result.Success(playlists);
        }

        // Artist: the followed-artists read stays on /me/following?type=artist (scope user-follow-read).
        string url = $"{SpotifyApiBase}/me/following?type=artist&limit={cappedLimit}";
        HttpResponseMessage? artistResponse = await SendAsync(
            HttpMethod.Get,
            url,
            token,
            cancellationToken
        );
        if (artistResponse is null)
            return Unavailable<IReadOnlyList<MusicFollowDto>>();
        if (artistResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return MissingScope<IReadOnlyList<MusicFollowDto>>();
        if (!artistResponse.IsSuccessStatusCode)
            return Unavailable<IReadOnlyList<MusicFollowDto>>();

        SpotifyFollowingResponse? json =
            await artistResponse.Content.ReadFromJsonAsync<SpotifyFollowingResponse>(
                cancellationToken: cancellationToken
            );
        if (json?.Artists?.Items is null)
            return Unavailable<IReadOnlyList<MusicFollowDto>>();

        IReadOnlyList<MusicFollowDto> artists = json
            .Artists.Items.Select(a => new MusicFollowDto(
                a.Id,
                a.Name,
                a.Images?.FirstOrDefault()?.Url
            ))
            .ToList()
            .AsReadOnly();

        return Result.Success(artists);
    }

    // ─── Manage failure mapping ──────────────────────────────────────────────

    private static Result<T> NotConnected<T>() =>
        Result.Failure<T>("Spotify is not connected for this channel.", "MISSING_SCOPE");

    private static Result NotConnected() =>
        Result.Failure("Spotify is not connected for this channel.", "MISSING_SCOPE");

    private static Result<T> MissingScope<T>() =>
        Result.Failure<T>("The Spotify connection is missing the required scope.", "MISSING_SCOPE");

    private static Result<T> Unavailable<T>() =>
        Result.Failure<T>("Spotify is temporarily unavailable.", "SERVICE_UNAVAILABLE");

    private static Result ManageOutcome(HttpResponseMessage? response, string notFoundSubject)
    {
        if (response is null)
            return Result.Failure("Spotify is temporarily unavailable.", "SERVICE_UNAVAILABLE");

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure(
                "The Spotify connection is missing the required scope.",
                "MISSING_SCOPE"
            );

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Failure($"{notFoundSubject} was not found on Spotify.", "NOT_FOUND");

        if (!response.IsSuccessStatusCode)
            return Result.Failure("Spotify is temporarily unavailable.", "SERVICE_UNAVAILABLE");

        return Result.Success();
    }

    // ─── Token management ────────────────────────────────────────────────────

    private async Task<string?> GetTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.Name == ProviderName
                && s.Enabled
                && s.AccessToken != null,
            cancellationToken
        );

        if (service is null)
        {
            _logger.LogDebug(
                "No Spotify service found for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        // Refresh if expiring within 5 minutes
        if (
            service.TokenExpiry.HasValue
            && service.TokenExpiry.Value <= _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5)
        )
        {
            string? refreshed = await RefreshTokenAsync(service, cancellationToken);
            if (refreshed is null)
                return null;
            return refreshed;
        }

        return service.AccessToken is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.AccessToken,
                new TokenProtectionContext(
                    service.BroadcasterId?.ToString() ?? "_platform",
                    ProviderName,
                    "access"
                ),
                cancellationToken
            )
            : null;
    }

    private async Task<string?> RefreshTokenAsync(
        Service service,
        CancellationToken cancellationToken
    )
    {
        if (service.RefreshToken is null)
            return null;

        string subjectId = service.BroadcasterId?.ToString() ?? "_platform";

        string? refreshToken = await _tokenProtector.TryUnprotectAsync(
            service.RefreshToken,
            new TokenProtectionContext(subjectId, ProviderName, "refresh"),
            cancellationToken
        );
        if (refreshToken is null)
            return null;

        // Client credentials required for refresh (stored on the service)
        string? clientId = service.ClientId is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientId,
                new TokenProtectionContext(subjectId, ProviderName, "client_id"),
                cancellationToken
            )
            : null;
        string? clientSecret = service.ClientSecret is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientSecret,
                new TokenProtectionContext(subjectId, ProviderName, "client_secret"),
                cancellationToken
            )
            : null;

        if (clientId is null || clientSecret is null)
        {
            _logger.LogWarning(
                "Spotify credentials not configured for broadcaster {BroadcasterId}",
                service.BroadcasterId
            );
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }
        );

        try
        {
            HttpResponseMessage response = await _http.PostAsync(
                SpotifyTokenEndpoint,
                form,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Spotify token refresh failed for {BroadcasterId}: {Status}",
                    service.BroadcasterId,
                    response.StatusCode
                );
                return null;
            }

            SpotifyTokenResponse? json =
                await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>(
                    cancellationToken: cancellationToken
                );
            if (json is null)
                return null;

            service.AccessToken = await _tokenProtector.ProtectAsync(
                json.AccessToken,
                new TokenProtectionContext(subjectId, ProviderName, "access"),
                cancellationToken
            );
            service.TokenExpiry = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn);

            // Refresh token may be rotated
            if (!string.IsNullOrEmpty(json.RefreshToken))
                service.RefreshToken = await _tokenProtector.ProtectAsync(
                    json.RefreshToken,
                    new TokenProtectionContext(subjectId, ProviderName, "refresh"),
                    cancellationToken
                );

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refreshed Spotify token for {BroadcasterId}",
                service.BroadcasterId
            );
            return json.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Exception refreshing Spotify token for {BroadcasterId}",
                service.BroadcasterId
            );
            return null;
        }
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────────

    private async Task<(
        HttpStatusCode? Status,
        SpotifyPaging<SpotifyPlaylist>? Page
    )> FetchPlaylistsPageAsync(
        string token,
        int offset,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string url = $"{SpotifyApiBase}/me/playlists?offset={offset}&limit={Math.Min(limit, 50)}";
        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            url,
            token,
            cancellationToken
        );
        if (response is null)
            return (null, null);

        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        SpotifyPaging<SpotifyPlaylist>? page = await response.Content.ReadFromJsonAsync<
            SpotifyPaging<SpotifyPlaylist>
        >(cancellationToken: cancellationToken);
        return (response.StatusCode, page);
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string url,
        string token,
        CancellationToken cancellationToken
    )
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new("Bearer", token);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (
                    response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values)
                    && int.TryParse(values.First(), out int retryAfter)
                )
                {
                    _logger.LogWarning("Spotify rate limited, retry-after={Seconds}s", retryAfter);
                    await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken);
                    // Retry once after backoff
                    request = new(method, url);
                    request.Headers.Authorization = new("Bearer", token);
                    return await _http.SendAsync(request, cancellationToken);
                }
            }

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Spotify API request failed: {Method} {Url}", method, url);
            return null;
        }
    }

    /// <summary>
    /// Player-command send with premium enforcement: a 403 whose error reason is
    /// <c>PREMIUM_REQUIRED</c> records the observed <c>spotify.premium=false</c> capability and
    /// throws <see cref="PremiumRequiredException"/> (mapped to <c>Failure("PREMIUM_REQUIRED")</c>
    /// at the first Result-typed surface); a successful player write records <c>true</c>.
    /// </summary>
    private async Task<HttpResponseMessage?> SendPlayerCommandAsync(
        HttpMethod method,
        string url,
        string token,
        object? body,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body);
        else if (method != HttpMethod.Get)
            request.Content = new StringContent(
                string.Empty,
                System.Text.Encoding.UTF8,
                "application/json"
            );

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Spotify player command failed: {Method} {Url}", method, url);
            return null;
        }

        if (
            response.StatusCode == HttpStatusCode.Forbidden
            && await IsPremiumRequiredAsync(response, cancellationToken)
        )
        {
            _capabilities.Report(broadcasterId, ProviderName, PremiumCapabilityKey, false);
            throw new PremiumRequiredException("Spotify");
        }

        if (response.IsSuccessStatusCode)
            _capabilities.Report(broadcasterId, ProviderName, PremiumCapabilityKey, true);

        return response;
    }

    /// <summary>Manage-surface send (library/playlists/follows) — JSON body support, no premium
    /// semantics (manage writes are not Premium-gated).</summary>
    private async Task<HttpResponseMessage?> SendManageAsync(
        HttpMethod method,
        string url,
        string token,
        object? body,
        CancellationToken cancellationToken
    )
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Spotify manage request failed: {Method} {Url}", method, url);
            return null;
        }
    }

    private static async Task<bool> IsPremiumRequiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            SpotifyErrorEnvelope? envelope =
                await response.Content.ReadFromJsonAsync<SpotifyErrorEnvelope>(
                    cancellationToken: cancellationToken
                );
            return string.Equals(
                envelope?.Error?.Reason,
                "PREMIUM_REQUIRED",
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ─── Mapping ─────────────────────────────────────────────────────────────

    /// <summary>Extracts a Spotify id of <paramref name="type"/> from a <c>spotify:{type}:…</c> URI,
    /// an <c>open.spotify.com/{type}/…</c> URL (with or without locale segment), or a bare id.</summary>
    private static string? ExtractId(string uriOrId, string type)
    {
        if (string.IsNullOrWhiteSpace(uriOrId))
            return null;

        string value = uriOrId.Trim();

        string uriPrefix = $"spotify:{type}:";
        if (value.StartsWith(uriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string id = value[uriPrefix.Length..];
            return id.Length > 0 && id.All(char.IsLetterOrDigit) ? id : null;
        }

        if (
            Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
            && url.Host.EndsWith("open.spotify.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            string[] segments = url.AbsolutePath.Trim('/').Split('/');
            int typeIndex = Array.IndexOf(segments, type);
            if (typeIndex < 0 || typeIndex + 1 >= segments.Length)
                return null;
            string id = segments[typeIndex + 1];
            return id.Length > 0 && id.All(char.IsLetterOrDigit) ? id : null;
        }

        // Bare id — Spotify ids are base62 alphanumerics.
        return value.All(char.IsLetterOrDigit) ? value : null;
    }

    /// <summary>Any accepted track input form → canonical <c>spotify:track:{id}</c> URI (falls back
    /// to the raw value when unparseable — the API then rejects it with a precise error).</summary>
    private static string NormalizeTrackUri(string uriOrId)
    {
        if (uriOrId.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            return uriOrId;

        string? id = ExtractId(uriOrId, "track");
        return id is null ? uriOrId : $"spotify:track:{id}";
    }

    private static MusicPlaylistDto MapPlaylistDto(SpotifyPlaylist playlist) =>
        new(
            playlist.Id,
            playlist.Name,
            string.IsNullOrEmpty(playlist.Description) ? null : playlist.Description,
            playlist.Public ?? false,
            playlist.ItemsPage?.Total ?? playlist.Tracks?.Total ?? 0,
            playlist.Images?.FirstOrDefault()?.Url,
            ProviderName
        );

    // isPlaying/progressMs are only known for a "currently playing" read (GetCurrentTrackAsync); a
    // SearchAsync/ResolveTrackAsync hit passes neither, leaving TrackInfo.IsPlaying/ProgressMs at
    // their false/0 defaults.
    private static TrackInfo MapToTrackInfo(
        SpotifyTrack track,
        bool isPlaying = false,
        int progressMs = 0,
        bool shuffleEnabled = false,
        MusicRepeatMode repeatMode = MusicRepeatMode.Off
    ) =>
        new()
        {
            TrackName = track.Name,
            Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
            Album = track.Album?.Name ?? string.Empty,
            TrackUri = track.Uri,
            AlbumArtUrl = track.Album?.Images?.FirstOrDefault()?.Url,
            DurationMs = track.DurationMs,
            Provider = ProviderName,
            ProviderTrackId = track.Id ?? string.Empty,
            IsExplicit = track.Explicit,
            IsAgeRestricted = false, // Spotify exposes no age-restriction flag; the gate is a YouTube knob.
            IsEmbeddable = true, // No embed constraint applies to Spotify drip-feed playback.
            IsPlaying = isPlaying,
            ProgressMs = progressMs,
            ShuffleEnabled = shuffleEnabled,
            RepeatMode = repeatMode,
        };

    // ─── Spotify API response models ─────────────────────────────────────────

    private sealed class SpotifySearchResponse
    {
        [JsonPropertyName("tracks")]
        public SpotifyPaging<SpotifyTrack>? Tracks { get; set; }
    }

    private sealed class SpotifyPaging<T>
    {
        [JsonPropertyName("items")]
        public List<T>? Items { get; set; }
    }

    // Shape of GET /me/player (full playback state) — a superset of /me/player/currently-playing that also
    // carries shuffle_state + repeat_state. Extra fields (device, context, …) are ignored by the deserializer.
    private sealed class SpotifyPlaybackState
    {
        [JsonPropertyName("item")]
        public SpotifyTrack? Item { get; set; }

        [JsonPropertyName("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonPropertyName("progress_ms")]
        public int ProgressMs { get; set; }

        [JsonPropertyName("shuffle_state")]
        public bool ShuffleState { get; set; }

        // "off" | "track" | "context" — null-tolerant; unknown/absent → Off.
        [JsonPropertyName("repeat_state")]
        public string? RepeatState { get; set; }
    }

    private sealed class SpotifyErrorEnvelope
    {
        [JsonPropertyName("error")]
        public SpotifyError? Error { get; set; }
    }

    private sealed class SpotifyError
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private sealed class SpotifyTrack
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = null!;

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }

        [JsonPropertyName("explicit")]
        public bool Explicit { get; set; }

        [JsonPropertyName("artists")]
        public List<SpotifyArtist> Artists { get; set; } = [];

        [JsonPropertyName("album")]
        public SpotifyAlbum? Album { get; set; }
    }

    private sealed class SpotifyArtist
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
    }

    private sealed class SpotifyAlbum
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }
    }

    private sealed class SpotifyImage
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = null!;
    }

    private sealed class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class SpotifyDevicesResponse
    {
        [JsonPropertyName("devices")]
        public List<SpotifyDevice>? Devices { get; set; }
    }

    private sealed class SpotifyDevice
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("type")]
        public string Type { get; set; } = null!;

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }

        [JsonPropertyName("volume_percent")]
        public int? VolumePercent { get; set; }
    }

    private sealed class SpotifyPlaylist
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("public")]
        public bool? Public { get; set; }

        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }

        // Full playlist objects now carry the item paging under "items"; the "tracks" form is the
        // deprecated legacy shape still present on simplified list objects. Count = whichever exists.
        [JsonPropertyName("items")]
        public SpotifyPlaylistTracks? ItemsPage { get; set; }

        [JsonPropertyName("tracks")]
        public SpotifyPlaylistTracks? Tracks { get; set; }
    }

    private sealed class SpotifyPlaylistTracks
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class SpotifySavedTrack
    {
        // GET /me/tracks wraps each item as { added_at, track: {…} } — only the track is mapped.
        [JsonPropertyName("track")]
        public SpotifyTrack? Track { get; set; }
    }

    private sealed class SpotifyFollowingResponse
    {
        [JsonPropertyName("artists")]
        public SpotifyFollowingArtists? Artists { get; set; }
    }

    private sealed class SpotifyFollowingArtists
    {
        [JsonPropertyName("items")]
        public List<SpotifyArtistSummary>? Items { get; set; }
    }

    private sealed class SpotifyArtistSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("images")]
        public List<SpotifyImage>? Images { get; set; }
    }
}
