// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) NoMercy Entertainment. All rights reserved.

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NoMercyBot.Api.Models;
using NoMercyBot.Application.Common.Interfaces;
using NoMercyBot.Application.Common.Models;
using NoMercyBot.Application.Services;
using ConfigEntity = NoMercyBot.Domain.Entities.Configuration;

namespace NoMercyBot.Api.Controllers.V1;

/// <summary>
/// System-level setup and readiness endpoints.
/// These are mostly anonymous — the system must be configurable before any user can log in.
/// Once setup is complete, destructive actions require admin authentication.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/system")]
[AllowAnonymous]
[Tags("System")]
public class SystemController : BaseController
{
    private readonly IAuthService _authService;
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;

    public SystemController(
        IAuthService authService,
        IApplicationDbContext db,
        IConfiguration config
    )
    {
        _authService = authService;
        _db = db;
        _config = config;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record SystemStatusDto(
        bool Ready,
        SystemChecks Checks
    );

    public record SystemChecks(
        CheckItem TwitchApp,
        CheckItem PlatformBot,
        CheckItem? Spotify,
        CheckItem? Discord
    );

    public record CheckItem(
        bool Ok,
        string Status,
        string? Detail
    );

    public record SaveCredentialRequest(
        string ClientId,
        string ClientSecret
    );

    // ── System readiness ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns system readiness status. Anonymous — must be callable before any user can log in.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<StatusResponseDto<SystemStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        // 1. Twitch app credentials (from DB first, then env/config fallback)
        string? twitchClientId = await GetSystemConfig("twitch.client_id", ct) ?? _config["Twitch:ClientId"];
        string? twitchClientSecret = await GetSystemConfig("twitch.client_secret", ct) ?? _config["Twitch:ClientSecret"];
        bool hasTwitch = !string.IsNullOrWhiteSpace(twitchClientId) && !string.IsNullOrWhiteSpace(twitchClientSecret);

        // 2. Platform bot (Service with Name="twitch_bot" and BroadcasterId IS NULL)
        bool hasPlatformBot = await _db.Services.AnyAsync(
            s => s.Name == "twitch_bot" && s.BroadcasterId == null && s.AccessToken != null,
            ct
        );

        // 3. Spotify app credentials (DB → IConfiguration → raw env)
        string? spotifyClientId = await GetSystemConfig("spotify.client_id", ct)
            ?? _config["Spotify:ClientId"]
            ?? Environment.GetEnvironmentVariable("Spotify__ClientId");
        string? spotifyClientSecret = await GetSystemConfig("spotify.client_secret", ct)
            ?? _config["Spotify:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("Spotify__ClientSecret");
        bool hasSpotify = !string.IsNullOrEmpty(spotifyClientId) && !string.IsNullOrEmpty(spotifyClientSecret);

        // 4. Discord app credentials (DB → IConfiguration → raw env)
        string? discordClientId = await GetSystemConfig("discord.client_id", ct)
            ?? _config["Discord:ClientId"]
            ?? Environment.GetEnvironmentVariable("Discord__ClientId");
        string? discordClientSecret = await GetSystemConfig("discord.client_secret", ct)
            ?? _config["Discord:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("Discord__ClientSecret");
        bool hasDiscord = !string.IsNullOrEmpty(discordClientId) && !string.IsNullOrEmpty(discordClientSecret);

        // System is ready when Twitch app and platform bot are both configured
        bool ready = hasTwitch && hasPlatformBot;

        var checks = new SystemChecks(
            TwitchApp: new CheckItem(
                hasTwitch,
                hasTwitch ? "configured" : "missing",
                hasTwitch ? "Client ID and secret are set" : "Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env"
            ),
            PlatformBot: new CheckItem(
                hasPlatformBot,
                hasPlatformBot ? "connected" : "disconnected",
                hasPlatformBot ? "Bot account is authorized" : "Authorize the bot's Twitch account"
            ),
            Spotify: new CheckItem(
                hasSpotify,
                hasSpotify ? "configured" : "not configured",
                hasSpotify ? "Client ID and secret are set" : "Optional — configure to enable song requests"
            ),
            Discord: new CheckItem(
                hasDiscord,
                hasDiscord ? "configured" : "not configured",
                hasDiscord ? "Client ID and secret are set" : "Optional — configure to enable Discord integration"
            )
        );

        return Ok(new StatusResponseDto<SystemStatusDto>
        {
            Data = new SystemStatusDto(ready, checks),
        });
    }

    // ── Platform bot OAuth ───────────────────────────────────────────────────

    /// <summary>Get the OAuth URL to authorize the platform bot account.</summary>
    [HttpGet("setup/bot/oauth-url")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GetBotOAuthUrl(CancellationToken ct)
    {
        string forwardedHost = Request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();
        string? host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : Request.Host.Value;
        string? scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : Request.Scheme;
        string publicBaseUrl = !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(scheme)
            ? $"{scheme}://{host}"
            : (_config["App:BaseUrl"] ?? "http://localhost:5080").TrimEnd('/');

        string url = await _authService.GetTwitchBotOAuthUrl(baseUrl: publicBaseUrl, cancellationToken: ct);
        return Ok(new StatusResponseDto<object> { Data = new { oauthUrl = url } });
    }

    /// <summary>Check the platform bot connection status.</summary>
    [HttpGet("setup/bot/status")]
    [ProducesResponseType<StatusResponseDto<BotStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBotStatus(CancellationToken ct)
    {
        Result<BotStatusDto> result = await _authService.GetBotStatusAsync(ct);
        return ResultResponse(result);
    }

    // ── Integration credentials ──────────────────────────────────────────────

    /// <summary>Save system-level Twitch app credentials.</summary>
    [HttpPut("setup/credentials/twitch")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTwitchCredentials(
        [FromBody] SaveTwitchCredentialRequest request,
        CancellationToken ct
    )
    {
        await UpsertSystemConfig("twitch.client_id", request.ClientId, ct);
        await UpsertSystemConfig("twitch.client_secret", request.ClientSecret, secure: true, ct: ct);
        if (!string.IsNullOrWhiteSpace(request.BotUsername))
            await UpsertSystemConfig("twitch.bot_username", request.BotUsername, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "Twitch credentials saved." });
    }

    public record SaveTwitchCredentialRequest(
        string ClientId,
        string ClientSecret,
        string? BotUsername
    );

    /// <summary>Save system-level Spotify app credentials.</summary>
    [HttpPut("setup/credentials/spotify")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSpotifyCredentials(
        [FromBody] SaveCredentialRequest request,
        CancellationToken ct
    )
    {
        await UpsertSystemConfig("spotify.client_id", request.ClientId, ct);
        await UpsertSystemConfig("spotify.client_secret", request.ClientSecret, secure: true, ct: ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "Spotify credentials saved." });
    }

    /// <summary>Save system-level Discord app credentials.</summary>
    [HttpPut("setup/credentials/discord")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDiscordCredentials(
        [FromBody] SaveCredentialRequest request,
        CancellationToken ct
    )
    {
        await UpsertSystemConfig("discord.client_id", request.ClientId, ct);
        await UpsertSystemConfig("discord.client_secret", request.ClientSecret, secure: true, ct: ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "Discord credentials saved." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string?> GetSystemConfig(string key, CancellationToken ct)
    {
        ConfigEntity? cfg = await _db.Configurations
            .FirstOrDefaultAsync(c => c.BroadcasterId == null && c.Key == key, ct);
        return cfg?.SecureValue ?? cfg?.Value;
    }

    private async Task UpsertSystemConfig(string key, string value, CancellationToken ct, bool secure = false)
    {
        ConfigEntity? cfg = await _db.Configurations
            .FirstOrDefaultAsync(c => c.BroadcasterId == null && c.Key == key, ct);

        if (cfg is null)
        {
            cfg = new ConfigEntity
            {
                BroadcasterId = null,
                Key = key,
            };
            _db.Configurations.Add(cfg);
        }

        if (secure)
            cfg.SecureValue = value;
        else
            cfg.Value = value;
    }
}
