// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Platform.Entities;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Tags("Integration OAuth")]
public class IntegrationOAuthController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IntegrationOAuthController> _logger;

    public IntegrationOAuthController(
        IApplicationDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<IntegrationOAuthController> logger)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Spotify ──────────────────────────────────────────────────────────────

    private const string SpotifyScopes =
        "user-read-playback-state user-modify-playback-state user-read-currently-playing playlist-read-private";

    /// <summary>
    /// Start the Spotify OAuth flow. Reads client credentials from the Configuration
    /// table and redirects to Spotify's authorization page.
    /// </summary>
    [HttpGet("channels/{channelId}/integrations/spotify/callback/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartSpotifyOAuth(string channelId, CancellationToken ct)
    {
        string? clientId = await GetConfigValueAsync(null, "spotify.client_id", ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequestResponse("Spotify client ID is not configured. Add a Configuration row with Key='spotify.client_id'.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/spotify/callback";

        var statePayload = JsonSerializer.Serialize(new { channel_id = channelId });
        string state = Convert.ToBase64String(Encoding.UTF8.GetBytes(statePayload));

        string authUrl = "https://accounts.spotify.com/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString(SpotifyScopes)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the Spotify OAuth callback. Exchanges the authorization code for
    /// access and refresh tokens, then stores them in the Service table.
    /// </summary>
    [HttpGet("integrations/spotify/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleSpotifyCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        string? channelId = ExtractChannelIdFromState(state);
        if (string.IsNullOrEmpty(channelId))
            return BadRequestResponse("Missing channel_id in OAuth state.");

        string? clientId = await GetConfigValueAsync(null, "spotify.client_id", ct);
        string? clientSecret = await GetConfigSecureValueAsync(null, "spotify.client_secret", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequestResponse("Spotify client credentials are not configured.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/spotify/callback";

        // Exchange code for token
        using var client = _httpClientFactory.CreateClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        });

        // Spotify uses Basic auth with client_id:client_secret
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("https://accounts.spotify.com/api/token", tokenRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange Spotify authorization code");
            return InternalServerErrorResponse("Failed to contact Spotify token endpoint.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Spotify token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            return InternalServerErrorResponse("Spotify token exchange failed.");
        }

        using var tokenDoc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        JsonElement root = tokenDoc.RootElement;

        string? accessToken = root.GetProperty("access_token").GetString();
        string? refreshToken = root.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        int expiresIn = root.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

        // Upsert Service record
        var service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "spotify" && s.BroadcasterId == channelId, ct);

        if (service is null)
        {
            service = new Service
            {
                Name = "spotify",
                BroadcasterId = channelId,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = accessToken;
        service.RefreshToken = refreshToken;
        service.TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
        service.Scopes = SpotifyScopes.Split(' ');
        service.Enabled = true;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Spotify OAuth completed for channel {ChannelId}", channelId);

        string frontendUrl = _config["App:FrontendUrl"] ?? _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?spotify_connected=true");
    }

    // ── Discord ──────────────────────────────────────────────────────────────

    private const string DiscordScopes = "bot guilds";

    /// <summary>
    /// Start the Discord OAuth flow. Redirects to Discord's authorization page
    /// with bot and guilds scopes.
    /// </summary>
    [HttpGet("channels/{channelId}/integrations/discord/callback/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartDiscordOAuth(string channelId, CancellationToken ct)
    {
        string? clientId = await GetConfigValueAsync(null, "discord.client_id", ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequestResponse("Discord client ID is not configured. Add a Configuration row with Key='discord.client_id'.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/discord/callback";

        var statePayload = JsonSerializer.Serialize(new { channel_id = channelId });
        string state = Convert.ToBase64String(Encoding.UTF8.GetBytes(statePayload));

        string authUrl = "https://discord.com/api/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString(DiscordScopes)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the Discord OAuth callback. Exchanges the authorization code for
    /// tokens and stores them. Also fetches guild info for the DiscordServerAuthorization.
    /// </summary>
    [HttpGet("integrations/discord/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleDiscordCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        string? channelId = ExtractChannelIdFromState(state);
        if (string.IsNullOrEmpty(channelId))
            return BadRequestResponse("Missing channel_id in OAuth state.");

        string? clientId = await GetConfigValueAsync(null, "discord.client_id", ct);
        string? clientSecret = await GetConfigSecureValueAsync(null, "discord.client_secret", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequestResponse("Discord client credentials are not configured.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/discord/callback";

        using var client = _httpClientFactory.CreateClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("https://discord.com/api/oauth2/token", tokenRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange Discord authorization code");
            return InternalServerErrorResponse("Failed to contact Discord token endpoint.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Discord token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            return InternalServerErrorResponse("Discord token exchange failed.");
        }

        using var tokenDoc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        JsonElement root = tokenDoc.RootElement;

        string? accessToken = root.GetProperty("access_token").GetString();
        string? refreshToken = root.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        int expiresIn = root.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 604800;
        string? guildId = root.TryGetProperty("guild", out var guildProp)
            && guildProp.TryGetProperty("id", out var guildIdProp) ? guildIdProp.GetString() : null;
        string? guildName = root.TryGetProperty("guild", out var guildProp2)
            && guildProp2.TryGetProperty("name", out var guildNameProp) ? guildNameProp.GetString() : null;

        // Store in Service table
        var service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "discord" && s.BroadcasterId == channelId, ct);

        if (service is null)
        {
            service = new Service
            {
                Name = "discord",
                BroadcasterId = channelId,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = accessToken;
        service.RefreshToken = refreshToken;
        service.TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
        service.Scopes = DiscordScopes.Split(' ');
        service.Enabled = true;

        // If the bot was added to a guild, store a DiscordServerAuthorization too
        if (!string.IsNullOrEmpty(guildId))
        {
            var discordAuth = await _db.DiscordServerAuthorizations.FirstOrDefaultAsync(
                d => d.BroadcasterId == channelId && d.GuildId == guildId, ct);

            if (discordAuth is null)
            {
                discordAuth = new DiscordServerAuthorization
                {
                    BroadcasterId = channelId,
                    GuildId = guildId,
                    GuildName = guildName ?? "Unknown",
                    Status = "active",
                    ApprovedAt = DateTime.UtcNow,
                };
                _db.DiscordServerAuthorizations.Add(discordAuth);
            }
            else
            {
                discordAuth.GuildName = guildName ?? discordAuth.GuildName;
                discordAuth.Status = "active";
                discordAuth.ApprovedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Discord OAuth completed for channel {ChannelId}, guild {GuildId}", channelId, guildId);

        string frontendUrl = _config["App:FrontendUrl"] ?? _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?discord_connected=true");
    }

    // ── YouTube ──────────────────────────────────────────────────────────────

    private const string YouTubeScopes = "https://www.googleapis.com/auth/youtube.readonly";

    /// <summary>
    /// Start the YouTube/Google OAuth flow. Redirects to Google's authorization page
    /// with YouTube readonly scope.
    /// </summary>
    [HttpGet("channels/{channelId}/integrations/youtube/callback/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartYouTubeOAuth(string channelId, CancellationToken ct)
    {
        string? clientId = await GetConfigValueAsync(null, "youtube.client_id", ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequestResponse("YouTube client ID is not configured. Add a Configuration row with Key='youtube.client_id'.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/youtube/callback";

        var statePayload = JsonSerializer.Serialize(new { channel_id = channelId });
        string state = Convert.ToBase64String(Encoding.UTF8.GetBytes(statePayload));

        string authUrl = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString(YouTubeScopes)}"
            + $"&access_type=offline"
            + $"&prompt=consent"
            + $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the YouTube/Google OAuth callback. Exchanges the authorization code for
    /// access and refresh tokens, then stores them in the Service table.
    /// </summary>
    [HttpGet("integrations/youtube/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleYouTubeCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        string? channelId = ExtractChannelIdFromState(state);
        if (string.IsNullOrEmpty(channelId))
            return BadRequestResponse("Missing channel_id in OAuth state.");

        string? clientId = await GetConfigValueAsync(null, "youtube.client_id", ct);
        string? clientSecret = await GetConfigSecureValueAsync(null, "youtube.client_secret", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequestResponse("YouTube client credentials are not configured.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/youtube/callback";

        using var client = _httpClientFactory.CreateClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange YouTube authorization code");
            return InternalServerErrorResponse("Failed to contact Google token endpoint.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("YouTube token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            return InternalServerErrorResponse("YouTube token exchange failed.");
        }

        using var tokenDoc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        JsonElement root = tokenDoc.RootElement;

        string? accessToken = root.GetProperty("access_token").GetString();
        string? refreshToken = root.TryGetProperty("refresh_token", out var rtProp) ? rtProp.GetString() : null;
        int expiresIn = root.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

        // Upsert Service record
        var service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "youtube" && s.BroadcasterId == channelId, ct);

        if (service is null)
        {
            service = new Service
            {
                Name = "youtube",
                BroadcasterId = channelId,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = accessToken;
        service.RefreshToken = refreshToken;
        service.TokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
        service.Scopes = YouTubeScopes.Split(' ');
        service.Enabled = true;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("YouTube OAuth completed for channel {ChannelId}", channelId);

        string frontendUrl = _config["App:FrontendUrl"] ?? _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?youtube_connected=true");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Read a plain-text Configuration value from the database.</summary>
    private async Task<string?> GetConfigValueAsync(string? broadcasterId, string key, CancellationToken ct)
    {
        string? dbValue = await _db.Configurations
            .Where(c => c.BroadcasterId == broadcasterId && c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(dbValue)) return dbValue;

        // Fallback to env: "spotify.client_id" → "Spotify:ClientId"
        return _config[ToPascalConfigKey(key)]
            ?? Environment.GetEnvironmentVariable(ToPascalEnvKey(key));
    }

    /// <summary>Read a secure (encrypted) Configuration value from the database.</summary>
    private async Task<string?> GetConfigSecureValueAsync(string? broadcasterId, string key, CancellationToken ct)
    {
        string? dbValue = await _db.Configurations
            .Where(c => c.BroadcasterId == broadcasterId && c.Key == key)
            .Select(c => c.SecureValue)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(dbValue)) return dbValue;

        return _config[ToPascalConfigKey(key)]
            ?? Environment.GetEnvironmentVariable(ToPascalEnvKey(key));
    }

    /// <summary>Convert "spotify.client_id" → "Spotify:ClientId" for IConfiguration.</summary>
    private static string ToPascalConfigKey(string key)
    {
        return string.Join(":", key.Split('.').Select(ToPascalCase));
    }

    /// <summary>Convert "spotify.client_id" → "Spotify__ClientId" for Environment.</summary>
    private static string ToPascalEnvKey(string key)
    {
        return string.Join("__", key.Split('.').Select(ToPascalCase));
    }

    private static string ToPascalCase(string segment)
    {
        return string.Concat(segment.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    /// <summary>Extract channel_id from a Base64-encoded JSON state parameter.</summary>
    private static string? ExtractChannelIdFromState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        try
        {
            byte[] decoded = Convert.FromBase64String(state);
            using var doc = JsonDocument.Parse(decoded);
            if (doc.RootElement.TryGetProperty("channel_id", out var cidEl))
                return cidEl.GetString();
        }
        catch
        {
            // State isn't our encoded payload — ignore.
        }

        return null;
    }
}
