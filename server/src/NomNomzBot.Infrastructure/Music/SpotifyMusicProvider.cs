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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Spotify Web API music provider.
/// Requires the broadcaster to have connected their Spotify account (Premium required for transport).
/// Token stored as Service(Name="spotify", BroadcasterId=broadcasterId).
///
/// Feb 2026 API changes respected:
/// - Search max 10 results per type
/// - Batch endpoints removed — no GET /tracks?ids=
/// - Browse endpoints removed
/// </summary>
public sealed class SpotifyMusicProvider
    : IMusicProvider,
        IMusicRemoteProvider,
        IMusicProviderManageApi
{
    private const string SpotifyApiBase = "https://api.spotify.com/v1";
    private const string SpotifyTokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ProviderName = "spotify";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SpotifyMusicProvider> _logger;

    public SpotifyMusicProvider(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<SpotifyMusicProvider> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _http = httpClientFactory.CreateClient("spotify");
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// Today's live Spotify surface. The §3.5 target set additionally holds <c>Volume</c>,
    /// <c>Previous</c>, and <c>Library</c> — withheld here until the Spotify-completeness slice wires
    /// PUT /me/player/volume, POST /me/player/previous, and the /me/tracks + /me/following calls, so
    /// the capability flags never promise more than the provider actually does.
    /// </summary>
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue
        | MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.Seek
        | MusicProviderCapabilities.NowPlaying
        | MusicProviderCapabilities.AcceptsSongRequests
        | MusicProviderCapabilities.Shuffle
        | MusicProviderCapabilities.Repeat
        | MusicProviderCapabilities.TransferDevice
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
            cancellationToken
        );
    }

    public Task PreviousAsync(Guid broadcasterId, CancellationToken cancellationToken = default)
    {
        // TODO next slice: Spotify-completeness — wire POST /me/player/previous and declare the
        // Previous capability. Unreachable today: the capability is withheld, so consumers gate this
        // member off with CAPABILITY_UNSUPPORTED before it is ever called.
        _logger.LogDebug("SpotifyMusicProvider.PreviousAsync is not wired yet");
        return Task.CompletedTask;
    }

    public async Task<TrackInfo?> GetCurrentTrackAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        string? token = await GetTokenAsync(broadcasterId, cancellationToken);
        if (token is null)
            return null;

        HttpResponseMessage? response = await SendAsync(
            HttpMethod.Get,
            $"{SpotifyApiBase}/me/player/currently-playing",
            token,
            cancellationToken
        );
        if (response is null || response.StatusCode == HttpStatusCode.NoContent)
            return null;

        if (!response.IsSuccessStatusCode)
            return null;

        SpotifyCurrentlyPlaying? json =
            await response.Content.ReadFromJsonAsync<SpotifyCurrentlyPlaying>(
                cancellationToken: cancellationToken
            );
        if (json?.Item is null)
            return null;

        return MapToTrackInfo(json.Item, json.IsPlaying, json.ProgressMs);
    }

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
        string? trackId = ExtractTrackId(uriOrId);
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
        await SendPlayerCommandAsync(HttpMethod.Put, url, token, null, cancellationToken);
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
        await SendPlayerCommandAsync(HttpMethod.Put, url, token, null, cancellationToken);
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
        await SendPlayerCommandAsync(HttpMethod.Put, url, token, null, cancellationToken);
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
                TrackCount = p.Tracks?.Total ?? 0,
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
            return Result.Failure<IReadOnlyList<MusicPlaylistDto>>(
                "Spotify is not connected for this channel.",
                "MISSING_SCOPE"
            );

        (HttpStatusCode? status, SpotifyPaging<SpotifyPlaylist>? page) =
            await FetchPlaylistsPageAsync(token, offset: 0, limit: 50, cancellationToken);

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure<IReadOnlyList<MusicPlaylistDto>>(
                "The Spotify connection is missing the playlist-read scope.",
                "MISSING_SCOPE"
            );

        if (page?.Items is null)
            return Result.Failure<IReadOnlyList<MusicPlaylistDto>>(
                "Spotify is temporarily unavailable.",
                "SERVICE_UNAVAILABLE"
            );

        IReadOnlyList<MusicPlaylistDto> playlists = page
            .Items.Select(p => new MusicPlaylistDto(
                p.Id,
                p.Name,
                string.IsNullOrEmpty(p.Description) ? null : p.Description,
                p.Public ?? false,
                p.Tracks?.Total ?? 0,
                p.Images?.FirstOrDefault()?.Url,
                ProviderName
            ))
            .ToList()
            .AsReadOnly();

        return Result.Success(playlists);
    }

    public Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        CreateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    ) =>
        // TODO next slice: Spotify §3.10 — wire POST /users/{id}/playlists.
        Task.FromResult(PlaylistWritesNotWired<MusicPlaylistDto>());

    public Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        UpdateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    ) =>
        // TODO next slice: Spotify §3.10 — wire PUT /playlists/{id}.
        Task.FromResult(PlaylistWritesNotWired<MusicPlaylistDto>());

    public Task<Result> DeletePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken cancellationToken = default
    ) =>
        // TODO next slice: Spotify §3.10 — wire DELETE /playlists/{id}/followers (unfollow-own).
        Task.FromResult(PlaylistWritesNotWired());

    public Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) =>
        // TODO next slice: Spotify §3.10 — wire POST /playlists/{id}/tracks.
        Task.FromResult(PlaylistWritesNotWired());

    public Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) =>
        // TODO next slice: Spotify §3.10 — wire DELETE /playlists/{id}/tracks.
        Task.FromResult(PlaylistWritesNotWired());

    // Library / follow members: the Library and Subscriptions capabilities are withheld above, so the
    // capability gate fails these closed before dispatch. Present for interface completeness only.
    // TODO next slice: Spotify §3.10 — wire PUT/DELETE /me/tracks and /me/following, then declare Library.

    public Task<Result> SaveTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LibraryNotWired());

    public Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LibraryNotWired());

    public Task<Result> RateTrackAsync(
        Guid broadcasterId,
        string provider,
        string trackUri,
        MusicRating rating,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LibraryNotWired());

    public Task<Result> FollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LibraryNotWired());

    public Task<Result> UnfollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LibraryNotWired());

    private static Result<T> PlaylistWritesNotWired<T>() =>
        Result.Failure<T>("Spotify playlist writes are not wired yet.", "CAPABILITY_UNSUPPORTED");

    private static Result PlaylistWritesNotWired() =>
        Result.Failure("Spotify playlist writes are not wired yet.", "CAPABILITY_UNSUPPORTED");

    private static Result LibraryNotWired() =>
        Result.Failure("Spotify library writes are not wired yet.", "CAPABILITY_UNSUPPORTED");

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

    private async Task<HttpResponseMessage?> SendPlayerCommandAsync(
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
        else if (method != HttpMethod.Get)
            request.Content = new StringContent(
                string.Empty,
                System.Text.Encoding.UTF8,
                "application/json"
            );

        try
        {
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Spotify player command failed: {Method} {Url}", method, url);
            return null;
        }
    }

    // ─── Mapping ─────────────────────────────────────────────────────────────

    /// <summary>Extracts the Spotify track id from a <c>spotify:track:…</c> URI, an
    /// <c>open.spotify.com/track/…</c> URL (with or without locale segment), or a bare id.</summary>
    private static string? ExtractTrackId(string uriOrId)
    {
        if (string.IsNullOrWhiteSpace(uriOrId))
            return null;

        string value = uriOrId.Trim();

        const string uriPrefix = "spotify:track:";
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
            int trackIndex = Array.IndexOf(segments, "track");
            if (trackIndex < 0 || trackIndex + 1 >= segments.Length)
                return null;
            string id = segments[trackIndex + 1];
            return id.Length > 0 && id.All(char.IsLetterOrDigit) ? id : null;
        }

        // Bare id — Spotify ids are base62 alphanumerics.
        return value.All(char.IsLetterOrDigit) ? value : null;
    }

    // isPlaying/progressMs are only known for a "currently playing" read (GetCurrentTrackAsync); a
    // SearchAsync/ResolveTrackAsync hit passes neither, leaving TrackInfo.IsPlaying/ProgressMs at
    // their false/0 defaults.
    private static TrackInfo MapToTrackInfo(
        SpotifyTrack track,
        bool isPlaying = false,
        int progressMs = 0
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

    private sealed class SpotifyCurrentlyPlaying
    {
        [JsonPropertyName("item")]
        public SpotifyTrack? Item { get; set; }

        [JsonPropertyName("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonPropertyName("progress_ms")]
        public int ProgressMs { get; set; }
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

        [JsonPropertyName("tracks")]
        public SpotifyPlaylistTracks? Tracks { get; set; }
    }

    private sealed class SpotifyPlaylistTracks
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }
}
