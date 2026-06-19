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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Spotify Web API music provider.
/// Requires the broadcaster to have connected their Spotify account (Premium required).
/// Token stored as Service(Name="spotify", BroadcasterId=broadcasterId).
///
/// Feb 2026 API changes respected:
/// - Search max 10 results per type
/// - Batch endpoints removed — no GET /tracks?ids=
/// - Browse endpoints removed
/// </summary>
public sealed class SpotifyMusicProvider : IMusicProvider
{
    private const string SpotifyApiBase = "https://api.spotify.com/v1";
    private const string SpotifyTokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ProviderName = "spotify";

    private readonly IApplicationDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly HttpClient _http;
    private readonly ILogger<SpotifyMusicProvider> _logger;

    public SpotifyMusicProvider(
        IApplicationDbContext db,
        IEncryptionService encryption,
        IHttpClientFactory httpClientFactory,
        ILogger<SpotifyMusicProvider> logger
    )
    {
        _db = db;
        _encryption = encryption;
        _http = httpClientFactory.CreateClient("spotify");
        _logger = logger;
    }

    public async Task PlayAsync(string broadcasterId, CancellationToken cancellationToken = default)
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

    public async Task PauseAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
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

    public async Task SkipAsync(string broadcasterId, CancellationToken cancellationToken = default)
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

    public async Task<TrackInfo?> GetCurrentTrackAsync(
        string broadcasterId,
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

        SpotifyCurrentlyPlaying? json = await response.Content.ReadFromJsonAsync<SpotifyCurrentlyPlaying>(
            cancellationToken: cancellationToken
        );
        if (json?.Item is null)
            return null;

        return MapToTrackInfo(json.Item);
    }

    public async Task<IReadOnlyList<TrackInfo>> SearchAsync(
        string broadcasterId,
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

        HttpResponseMessage? response = await SendAsync(HttpMethod.Get, url, token, cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
            return [];

        SpotifySearchResponse? json = await response.Content.ReadFromJsonAsync<SpotifySearchResponse>(
            cancellationToken: cancellationToken
        );
        if (json?.Tracks?.Items is null)
            return [];

        return json.Tracks.Items.Where(t => t is not null).Select(MapToTrackInfo).ToList();
    }

    public async Task<bool> AddToQueueAsync(
        string broadcasterId,
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

    // ─── Token management ────────────────────────────────────────────────────

    private async Task<string?> GetTokenAsync(
        string broadcasterId,
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
            && service.TokenExpiry.Value <= DateTime.UtcNow.AddMinutes(5)
        )
        {
            string? refreshed = await RefreshTokenAsync(service, cancellationToken);
            if (refreshed is null)
                return null;
            return refreshed;
        }

        return service.AccessToken is not null ? _encryption.TryDecrypt(service.AccessToken) : null;
    }

    private async Task<string?> RefreshTokenAsync(
        Domain.Platform.Entities.Service service,
        CancellationToken cancellationToken
    )
    {
        if (service.RefreshToken is null)
            return null;

        string? refreshToken = _encryption.TryDecrypt(service.RefreshToken);
        if (refreshToken is null)
            return null;

        // Client credentials required for refresh (stored on the service)
        string? clientId = service.ClientId is not null
            ? _encryption.TryDecrypt(service.ClientId)
            : null;
        string? clientSecret = service.ClientSecret is not null
            ? _encryption.TryDecrypt(service.ClientSecret)
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
            HttpResponseMessage response = await _http.PostAsync(SpotifyTokenEndpoint, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Spotify token refresh failed for {BroadcasterId}: {Status}",
                    service.BroadcasterId,
                    response.StatusCode
                );
                return null;
            }

            SpotifyTokenResponse? json = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>(
                cancellationToken: cancellationToken
            );
            if (json is null)
                return null;

            service.AccessToken = _encryption.Encrypt(json.AccessToken);
            service.TokenExpiry = DateTime.UtcNow.AddSeconds(json.ExpiresIn);

            // Refresh token may be rotated
            if (!string.IsNullOrEmpty(json.RefreshToken))
                service.RefreshToken = _encryption.Encrypt(json.RefreshToken);

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

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string url,
        string token,
        CancellationToken cancellationToken
    )
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new(
            "Bearer",
            token
        );

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
                    request.Headers.Authorization =
                        new("Bearer", token);
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
        request.Headers.Authorization = new(
            "Bearer",
            token
        );

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

    private static TrackInfo MapToTrackInfo(SpotifyTrack track) =>
        new()
        {
            TrackName = track.Name,
            Artist = string.Join(", ", track.Artists.Select(a => a.Name)),
            Album = track.Album?.Name ?? string.Empty,
            TrackUri = track.Uri,
            AlbumArtUrl = track.Album?.Images?.FirstOrDefault()?.Url,
            DurationMs = track.DurationMs,
            Provider = ProviderName,
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
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = null!;

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }

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
}
