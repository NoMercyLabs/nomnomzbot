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
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Spotify Web API music provider.
/// Requires the broadcaster to have connected their Spotify account (Premium required for transport —
/// a player write rejected 403/<c>PREMIUM_REQUIRED</c> throws <see cref="PremiumRequiredException"/>,
/// which the first Result-typed surface maps to <c>Failure("PREMIUM_REQUIRED")</c>, and flips the
/// observed <c>spotify.premium</c> capability for the integrations status surface).
/// Tokens live in the crypto vault (<c>IIntegrationTokenVault</c>) under the channel's <c>IntegrationConnection</c>
/// (Provider="spotify", BroadcasterId=broadcasterId) — the vault is the single token source (S003; no
/// <c>Service</c>-row read).
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

    /// <summary>S003 — the live-call-observed auth signal, distinct from <see cref="PremiumCapabilityKey"/>:
    /// a 401 means the token itself is dead. Reported false the instant any call succeeds again, so a
    /// stale <c>needs_reauth</c> never outlives the connection that triggered it.</summary>
    internal const string NeedsReauthCapabilityKey = "auth.needs_reauth";

    /// <summary>S003 — a live 403 whose reason is NOT <c>PREMIUM_REQUIRED</c>: the token is alive but the
    /// account/grant lacks permission for the call. Cleared the same way as <see cref="NeedsReauthCapabilityKey"/>.</summary>
    internal const string ForbiddenCapabilityKey = "auth.forbidden";

    private const int LibraryUrisPerRequest = 40; // /me/library hard cap per live reference
    private const int ContainsIdsPerRequest = 50; // GET /me/tracks/contains hard cap per live reference
    private const int SavedTracksPerPage = 50; // GET /me/tracks limit hard cap per live reference

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IIntegrationCapabilityStore _capabilities;
    private readonly ILastActiveSpotifyDeviceTracker _lastActiveDevice;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SpotifyMusicProvider> _logger;
    private readonly Identity.IConnectionRefreshGate _refreshGate;

    public SpotifyMusicProvider(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        IIntegrationCapabilityStore capabilities,
        ILastActiveSpotifyDeviceTracker lastActiveDevice,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<SpotifyMusicProvider> logger,
        ISystemCredentialsProvider credentials,
        Identity.IConnectionRefreshGate refreshGate
    )
    {
        _db = db;
        _vault = vault;
        _capabilities = capabilities;
        _lastActiveDevice = lastActiveDevice;
        _http = httpClientFactory.CreateClient("spotify");
        _timeProvider = timeProvider;
        _logger = logger;
        _credentials = credentials;
        _refreshGate = refreshGate;
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
        | MusicProviderCapabilities.Playlists
        | MusicProviderCapabilities.EmbeddedPlayback;

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
            broadcasterId,
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

        if (!string.IsNullOrWhiteSpace(json.Device?.Id))
            _lastActiveDevice.Remember(broadcasterId, json.Device.Id);

        SpotifyDisallows? disallows = json.Actions?.Disallows;
        return MapToTrackInfo(
            json.Item,
            json.IsPlaying,
            json.ProgressMs,
            json.ShuffleState,
            ParseRepeatState(json.RepeatState),
            json.Device?.VolumePercent,
            canSetShuffle: disallows?.TogglingShuffle != true,
            // Repeat is a single cycling toggle (Off→Track→Context→Off) here, so it's blocked only when
            // Spotify disallows moving to BOTH repeat targets — one still-open target keeps cycling usable.
            canSetRepeat: !(
                disallows?.TogglingRepeatTrack == true && disallows?.TogglingRepeatContext == true
            ),
            canSkipNext: disallows?.SkippingNext != true,
            canSkipPrevious: disallows?.SkippingPrev != true,
            canSeek: disallows?.Seeking != true,
            canPause: disallows?.Pausing != true,
            canResume: disallows?.Resuming != true
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
            broadcasterId,
            cancellationToken
        );
        if (response is null || !response.IsSuccessStatusCode)
            return [];

        SpotifySearchResponse? json = await ReadJsonSafeAsync<SpotifySearchResponse>(
            response,
            "search",
            cancellationToken
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
            broadcasterId,
            cancellationToken
        );
        if (response is null || !response.IsSuccessStatusCode)
            return null;

        SpotifyTrack? track = await ReadJsonSafeAsync<SpotifyTrack>(
            response,
            "tracks/{id}",
            cancellationToken
        );
        return track is null ? null : MapToTrackInfo(track);
    }

    /// <summary>
    /// Deserializes a successful response's JSON body, failing to null (never throwing) on a malformed or
    /// empty body — an erroring/misbehaving provider must degrade the SR request to "not found", not crash
    /// the command pipeline. Mirrors <see cref="YouTubeMusicProvider"/>'s already-safe <c>GetJsonAsync</c>;
    /// applied here to the two members on the !sr hot path (search, resolve-by-link).
    /// </summary>
    private async Task<T?> ReadJsonSafeAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken
    )
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Spotify Web API {Operation} returned an unparseable body",
                operation
            );
            return null;
        }
    }

    public async Task<string?> GetEmbeddedPlaybackTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Guid? connectionId = await FindConnectionIdAsync(broadcasterId, cancellationToken);
        if (connectionId is null)
        {
            _logger.LogDebug(
                "GetEmbeddedPlaybackTokenAsync: no Spotify connection for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        List<string> grantedScopes =
            await _db
                .IntegrationConnections.Where(c => c.Id == connectionId.Value)
                .Select(c => c.Scopes)
                .FirstOrDefaultAsync(cancellationToken)
            ?? [];

        // The SDK becomes a real Connect device and streams audio — deliberately gated on its own scope
        // (never implied by the playback-control scopes already granted), so an existing connection made
        // before this feature shipped is never silently handed a token wider than what the streamer actually
        // consented to.
        if (!grantedScopes.Contains("streaming"))
        {
            _logger.LogDebug(
                "GetEmbeddedPlaybackTokenAsync: broadcaster {BroadcasterId} has not granted the streaming scope",
                broadcasterId
            );
            return null;
        }

        return await GetTokenAsync(broadcasterId, cancellationToken);
    }

    /// <summary>
    /// Pushes a track onto Spotify's player queue. A dead/expired connection throws
    /// <see cref="MusicAuthenticationFailedException"/> (no token to send), no active playback device
    /// throws <see cref="NoActiveDeviceException"/> (404 reason <c>NO_ACTIVE_DEVICE</c>, after
    /// <see cref="SendPlayerCommandAsync"/>'s own last-known-device retry already failed), and a
    /// non-Premium account throws <see cref="PremiumRequiredException"/> (raised inside
    /// <see cref="SendPlayerCommandAsync"/>). Any other unsuccessful response (an unrecognised
    /// provider failure — 5xx, malformed body, …) returns <c>false</c> rather than throwing, so the
    /// caller still gets a definite outcome instead of the request silently vanishing.
    /// </summary>
    public async Task<bool> AddToQueueAsync(
        Guid broadcasterId,
        string trackUri,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            throw new MusicAuthenticationFailedException(ProviderName);

        string url = $"{SpotifyApiBase}/me/player/queue?uri={Uri.EscapeDataString(trackUri)}";
        HttpResponseMessage? response = await SendPlayerCommandAsync(
            HttpMethod.Post,
            url,
            token,
            null,
            broadcasterId,
            cancellationToken
        );

        if (response?.IsSuccessStatusCode == true)
            return true;

        if (
            response is { StatusCode: HttpStatusCode.NotFound }
            && await IsNoActiveDeviceAsync(response, cancellationToken)
        )
            throw new NoActiveDeviceException(ProviderName);

        // S003 — a live 401 means the connection died mid-session (GetTokenAsync only catches an
        // already-dead token; this catches Spotify rejecting a still-fresh-looking one). A live 403
        // reaching here is never PREMIUM_REQUIRED (SendPlayerCommandAsync already intercepted and threw
        // for that reason before returning) — it means the grant lacks permission for this call.
        if (response?.StatusCode == HttpStatusCode.Unauthorized)
            throw new MusicAuthenticationFailedException(ProviderName);
        if (response?.StatusCode == HttpStatusCode.Forbidden)
            throw new MusicForbiddenException(ProviderName);

        return false;
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
        HttpResponseMessage? response = await SendPlayerCommandAsync(
            HttpMethod.Put,
            url,
            token,
            new { device_ids = new[] { deviceId }, play },
            broadcasterId,
            cancellationToken
        );

        // Unlike the fire-and-forget player commands (play/pause/skip/…), a transfer whose
        // device_id Spotify rejects (most commonly 404 — Spotify Connect device ids rotate on
        // every client reconnect) must NOT be reported as a success: the caller picked a device
        // from a list that's gone stale, and swallowing the failure here made the plugin claim
        // the switch happened while Spotify silently kept playing on the old device.
        if (response is null || !response.IsSuccessStatusCode)
            throw new DeviceTransferFailedException(ProviderName, (int?)response?.StatusCode);
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
            broadcasterId,
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
            await FetchPlaylistsPageAsync(token, broadcasterId, offset, limit, cancellationToken);
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
            await FetchPlaylistsPageAsync(
                token,
                broadcasterId,
                offset: 0,
                limit: 50,
                cancellationToken
            );

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
            broadcasterId,
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
            broadcasterId,
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
                broadcasterId,
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
                await FetchPlaylistsPageAsync(
                    token,
                    broadcasterId,
                    0,
                    cappedLimit,
                    cancellationToken
                );
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
            broadcasterId,
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

    // ─── Token management (S003 — the vault is the single token source; no Service-row read) ──

    /// <summary>The channel's own (non-revoked) Spotify <c>IntegrationConnection</c> id, or null when
    /// nothing is connected. The single lookup every token-management member resolves through.</summary>
    private async Task<Guid?> FindConnectionIdAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    ) =>
        await _db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Spotify
                && c.BroadcasterId == broadcasterId
                && c.Status != AuthEnums.IntegrationStatus.Revoked
            )
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<string?> GetTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        Guid? connectionId = await FindConnectionIdAsync(broadcasterId, cancellationToken);
        if (connectionId is null)
        {
            _logger.LogDebug(
                "No Spotify connection found for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(
            connectionId.Value,
            cancellationToken
        );
        if (access.IsFailure)
        {
            _logger.LogDebug(
                "Spotify access token unavailable for broadcaster {BroadcasterId}: {Error}",
                broadcasterId,
                access.ErrorMessage
            );
            return null;
        }

        bool expiring =
            access.Value.IsExpired
            || (
                access.Value.ExpiresAt is { } expiresAt
                && expiresAt <= _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5)
            );
        if (!expiring)
            return access.Value.Value;

        return await RefreshTokenAsync(connectionId.Value, broadcasterId, cancellationToken);
    }

    private async Task<string?> RefreshTokenAsync(
        Guid connectionId,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        // S036/S036b — serialize refreshes of the SAME connection. Two concurrent callers racing a refresh
        // could both post the same Spotify refresh token; re-checking the vaulted token under the gate lets
        // the loser reuse the winner's freshly-vaulted token instead of refreshing again.
        using IDisposable gate = await _refreshGate.AcquireAsync(
            $"spotify:{connectionId}",
            cancellationToken
        );

        Result<DecryptedTokenDto> current = await _vault.GetAccessTokenAsync(
            connectionId,
            cancellationToken
        );
        if (
            current.IsSuccess
            && !current.Value.IsExpired
            && current.Value.ExpiresAt is { } currentExpiresAt
            && currentExpiresAt > _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5)
        )
            return current.Value.Value;

        Result<DecryptedTokenDto> refresh = await _vault.GetRefreshTokenAsync(
            connectionId,
            cancellationToken
        );
        if (refresh.IsFailure)
            return null;

        SystemAppCredentials? app = await _credentials.GetAsync(ProviderName, cancellationToken);
        if (app is null)
        {
            _logger.LogWarning(
                "Spotify credentials not configured for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh.Value.Value,
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
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
                    broadcasterId,
                    response.StatusCode
                );
                await _vault.MarkRefreshFailureAsync(
                    connectionId,
                    $"Spotify refresh failed ({(int)response.StatusCode})",
                    cancellationToken
                );
                return null;
            }

            SpotifyTokenResponse? json =
                await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>(
                    cancellationToken: cancellationToken
                );
            if (json is null)
            {
                await _vault.MarkRefreshFailureAsync(
                    connectionId,
                    "Spotify refresh returned an unexpected body",
                    cancellationToken
                );
                return null;
            }

            // Refresh token may be rotated — a null/empty one from Spotify keeps the existing vaulted one.
            await _vault.StoreTokensAsync(
                connectionId,
                new(
                    json.AccessToken,
                    string.IsNullOrEmpty(json.RefreshToken) ? null : json.RefreshToken,
                    AppToken: null,
                    AccessExpiresAt: _timeProvider
                        .GetUtcNow()
                        .UtcDateTime.AddSeconds(json.ExpiresIn)
                ),
                grantedScopes: null,
                cancellationToken
            );

            _logger.LogInformation("Refreshed Spotify token for {BroadcasterId}", broadcasterId);
            return json.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Exception refreshing Spotify token for {BroadcasterId}",
                broadcasterId
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
        Guid broadcasterId,
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
            broadcasterId,
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
        Guid broadcasterId,
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
                    response = await _http.SendAsync(request, cancellationToken);
                }
            }

            // Buffer up front so ClassifyAuthAsync's own body read (403 premium-vs-forbidden
            // disambiguation) never disturbs the caller's own subsequent read of the same response.
            if (response.Content is not null)
                await response.Content.LoadIntoBufferAsync();
            await ClassifyAuthAsync(response, broadcasterId, cancellationToken);

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Spotify API request failed: {Method} {Url}", method, url);
            return null;
        }
    }

    /// <summary>
    /// S003 — the read-path (GET) auth classification, mirroring <see cref="SendPlayerCommandAsync"/>'s
    /// end-of-call classification for player writes. A live 401 means the token itself is dead
    /// (<see cref="NeedsReauthCapabilityKey"/>); a live 403 whose reason is NOT <c>PREMIUM_REQUIRED</c>
    /// means the token is alive but lacks permission (<see cref="ForbiddenCapabilityKey"/>); any
    /// success clears both, so a resolved connection is never left showing a stale broken state.
    /// </summary>
    private async Task ClassifyAuthAsync(
        HttpResponseMessage response,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _capabilities.Report(broadcasterId, ProviderName, NeedsReauthCapabilityKey, true);
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (!await IsPremiumRequiredAsync(response, cancellationToken))
                _capabilities.Report(broadcasterId, ProviderName, ForbiddenCapabilityKey, true);
            return;
        }

        if (response.IsSuccessStatusCode)
        {
            _capabilities.Report(broadcasterId, ProviderName, NeedsReauthCapabilityKey, false);
            _capabilities.Report(broadcasterId, ProviderName, ForbiddenCapabilityKey, false);
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
        HttpRequestMessage BuildRequest(HttpMethod m, string u, object? b)
        {
            HttpRequestMessage req = new(m, u);
            req.Headers.Authorization = new("Bearer", token);
            if (b is not null)
                req.Content = JsonContent.Create(b);
            else if (m != HttpMethod.Get)
                req.Content = new StringContent(
                    string.Empty,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
            return req;
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(BuildRequest(method, url, body), cancellationToken);
            // Buffer the body up front: a failed response's error envelope is inspected here
            // (premium/no-active-device) AND, on AddToQueueAsync, a second time by the caller to
            // distinguish NO_ACTIVE_DEVICE from any other provider failure — a live network stream
            // is forward-only, so without buffering the caller's re-read would see an empty body.
            if (response.Content is not null)
                await response.Content.LoadIntoBufferAsync();
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

        // Nothing is selected as the active Spotify device (e.g. the streamer closed every client) — retry
        // once against the last device we ever saw active for this channel, rather than surfacing a failure
        // for something the streamer never had to think about with a phone or desktop app.
        if (
            response.StatusCode == HttpStatusCode.NotFound
            && await IsNoActiveDeviceAsync(response, cancellationToken)
            && _lastActiveDevice.TryGet(broadcasterId, out string? rememberedDeviceId)
        )
        {
            HttpResponseMessage transfer;
            try
            {
                transfer = await _http.SendAsync(
                    BuildRequest(
                        HttpMethod.Put,
                        $"{SpotifyApiBase}/me/player",
                        new { device_ids = new[] { rememberedDeviceId }, play = false }
                    ),
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Spotify auto-transfer to last device failed");
                transfer = response; // fall through with the original NO_ACTIVE_DEVICE response
            }

            if (transfer.IsSuccessStatusCode)
            {
                response = await _http.SendAsync(
                    BuildRequest(method, url, body),
                    cancellationToken
                );
                if (response.Content is not null)
                    await response.Content.LoadIntoBufferAsync();
            }
        }

        if (response.IsSuccessStatusCode)
            _capabilities.Report(broadcasterId, ProviderName, PremiumCapabilityKey, true);

        // S003 — a 401 here means the token itself is dead; a non-premium 403 falls through to here
        // untouched (the premium branch above already intercepted and threw for that specific reason).
        await ClassifyAuthAsync(response, broadcasterId, cancellationToken);

        return response;
    }

    private static Task<bool> IsNoActiveDeviceAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    ) => ErrorReasonIsAsync(response, "NO_ACTIVE_DEVICE", cancellationToken);

    /// <summary>
    /// Reads Spotify's <c>{"error":{"reason":"..."}}</c> envelope off an unsuccessful response and
    /// compares its <c>reason</c>. Uses <c>ReadAsStringAsync</c> + <c>JsonSerializer.Deserialize</c>
    /// rather than <c>HttpContent.ReadFromJsonAsync</c> because the latter disposes the content stream
    /// once read — this response is inspected for more than one reason across the retry/recovery path
    /// (premium check, no-active-device check, and again by <see cref="AddToQueueAsync"/> to
    /// distinguish its own typed failure), so every caller needs a repeatable read of the same body.
    /// </summary>
    private static async Task<bool> ErrorReasonIsAsync(
        HttpResponseMessage response,
        string reason,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            SpotifyErrorEnvelope? envelope = JsonSerializer.Deserialize<SpotifyErrorEnvelope>(body);
            return string.Equals(
                envelope?.Error?.Reason,
                reason,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception)
        {
            return false;
        }
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

    private static Task<bool> IsPremiumRequiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    ) => ErrorReasonIsAsync(response, "PREMIUM_REQUIRED", cancellationToken);

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
        MusicRepeatMode repeatMode = MusicRepeatMode.Off,
        int? volumePercent = null,
        bool canSetShuffle = true,
        bool canSetRepeat = true,
        bool canSkipNext = true,
        bool canSkipPrevious = true,
        bool canSeek = true,
        bool canPause = true,
        bool canResume = true
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
            ArtistId = track.Artists.FirstOrDefault()?.Id,
            IsExplicit = track.Explicit,
            IsAgeRestricted = false, // Spotify exposes no age-restriction flag; the gate is a YouTube knob.
            IsEmbeddable = true, // No embed constraint applies to Spotify drip-feed playback.
            IsPlaying = isPlaying,
            ProgressMs = progressMs,
            ShuffleEnabled = shuffleEnabled,
            RepeatMode = repeatMode,
            VolumePercent = volumePercent,
            CanSetShuffle = canSetShuffle,
            CanSetRepeat = canSetRepeat,
            CanSkipNext = canSkipNext,
            CanSkipPrevious = canSkipPrevious,
            CanSeek = canSeek,
            CanPause = canPause,
            CanResume = canResume,
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

        [JsonPropertyName("device")]
        public SpotifyPlaybackDevice? Device { get; set; }

        [JsonPropertyName("actions")]
        public SpotifyPlaybackActions? Actions { get; set; }
    }

    private sealed class SpotifyPlaybackDevice
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("volume_percent")]
        public int? VolumePercent { get; set; }
    }

    // Spotify only sets a key to `true` when that action is currently BLOCKED; an absent/false key means
    // allowed. Reflects real restrictions — ads, non-Premium accounts, restricted markets, single-track
    // context — that generic capability flags can't see.
    private sealed class SpotifyPlaybackActions
    {
        [JsonPropertyName("disallows")]
        public SpotifyDisallows? Disallows { get; set; }
    }

    private sealed class SpotifyDisallows
    {
        [JsonPropertyName("resuming")]
        public bool Resuming { get; set; }

        [JsonPropertyName("pausing")]
        public bool Pausing { get; set; }

        [JsonPropertyName("seeking")]
        public bool Seeking { get; set; }

        [JsonPropertyName("skipping_next")]
        public bool SkippingNext { get; set; }

        [JsonPropertyName("skipping_prev")]
        public bool SkippingPrev { get; set; }

        [JsonPropertyName("toggling_shuffle")]
        public bool TogglingShuffle { get; set; }

        [JsonPropertyName("toggling_repeat_track")]
        public bool TogglingRepeatTrack { get; set; }

        [JsonPropertyName("toggling_repeat_context")]
        public bool TogglingRepeatContext { get; set; }
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
        [JsonPropertyName("id")]
        public string? Id { get; set; }

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
