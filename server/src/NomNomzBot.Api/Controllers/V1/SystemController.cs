// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

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
    private readonly ITokenProtector _protector;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IHostEnvironment _env;
    private readonly ITwitchOAuthStateService _oauthState;

    public SystemController(
        IAuthService authService,
        IApplicationDbContext db,
        IConfiguration config,
        ITokenProtector protector,
        ISystemCredentialsProvider credentials,
        IHostEnvironment env,
        ITwitchOAuthStateService oauthState
    )
    {
        _authService = authService;
        _db = db;
        _config = config;
        _protector = protector;
        _credentials = credentials;
        _env = env;
        _oauthState = oauthState;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record SystemStatusDto(bool Ready, SystemChecks Checks);

    public record SystemChecks(
        CheckItem TwitchApp,
        CheckItem PlatformBot,
        CheckItem? Spotify,
        CheckItem? Discord,
        CheckItem? YouTube
    );

    public record CheckItem(bool Ok, string Status, string? Detail);

    public record SaveCredentialRequest(string ClientId, string ClientSecret);

    // ── System readiness ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns system readiness status. Anonymous — must be callable before any user can log in.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<StatusResponseDto<SystemStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        SetupState st = await ComputeSetupStateAsync(ct);
        bool hasTwitch = st.HasTwitch;
        bool hasPlatformBot = st.HasPlatformBot;
        bool hasSpotify = st.HasSpotify;
        bool hasDiscord = st.HasDiscord;
        bool hasYouTube = st.HasYouTube;

        // System is ready when Twitch app and platform bot are both configured
        bool ready = hasTwitch && hasPlatformBot;

        SystemChecks checks = new SystemChecks(
            TwitchApp: new CheckItem(
                hasTwitch,
                hasTwitch ? "configured" : "missing",
                hasTwitch ? "Client ID and secret are set"
                    : _env.IsDevelopment() ? "Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env"
                    : "Not configured"
            ),
            PlatformBot: new CheckItem(
                hasPlatformBot,
                hasPlatformBot ? "connected" : "disconnected",
                hasPlatformBot ? "Bot account is authorized" : "Authorize the bot's Twitch account"
            ),
            Spotify: new CheckItem(
                hasSpotify,
                hasSpotify ? "configured" : "not configured",
                hasSpotify
                    ? "Client ID and secret are set"
                    : "Optional — configure to enable song requests"
            ),
            Discord: new CheckItem(
                hasDiscord,
                hasDiscord ? "configured" : "not configured",
                hasDiscord
                    ? "Client ID and secret are set"
                    : "Optional — configure to enable Discord integration"
            ),
            YouTube: new CheckItem(
                hasYouTube,
                hasYouTube ? "configured" : "not configured",
                hasYouTube
                    ? "Client ID and secret are set"
                    : "Optional — configure to enable the YouTube music provider"
            )
        );

        return Ok(
            new StatusResponseDto<SystemStatusDto> { Data = new SystemStatusDto(ready, checks) }
        );
    }

    /// <summary>
    /// The self-describing onboarding wizard: the ordered steps, each with its copy, step-by-step instructions, the
    /// exact redirect URI to register, the API call that satisfies it, the input fields, and its live completion
    /// state. A dashboard renders the entire first-time-setup flow from this single call.
    /// </summary>
    [HttpGet("setup/wizard")]
    [ProducesResponseType<StatusResponseDto<SetupWizardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWizard(CancellationToken ct)
    {
        SetupState st = await ComputeSetupStateAsync(ct);
        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";

        return Ok(
            new StatusResponseDto<SetupWizardDto>
            {
                Data = SetupWizard.Build(
                    st.HasTwitch,
                    st.HasPlatformBot,
                    st.HasSpotify,
                    st.HasDiscord,
                    st.HasYouTube,
                    baseUrl
                ),
            }
        );
    }

    private async Task<SetupState> ComputeSetupStateAsync(CancellationToken ct)
    {
        // One resolution path: the credentials provider reads the wizard-vaulted DB rows first, then the
        // {Provider}:ClientId/Secret config fallback, returning a value only when BOTH fields are present —
        // which is exactly the "configured" predicate each step needs.
        bool hasTwitch = await _credentials.GetAsync("twitch", ct) is not null;
        bool hasSpotify = await _credentials.GetAsync("spotify", ct) is not null;
        bool hasDiscord = await _credentials.GetAsync("discord", ct) is not null;
        bool hasYouTube = await _credentials.GetAsync("youtube", ct) is not null;

        bool hasPlatformBot = await _db.Services.AnyAsync(
            s => s.Name == "twitch_bot" && s.BroadcasterId == null && s.AccessToken != null,
            ct
        );

        return new SetupState(hasTwitch, hasPlatformBot, hasSpotify, hasDiscord, hasYouTube);
    }

    private sealed record SetupState(
        bool HasTwitch,
        bool HasPlatformBot,
        bool HasSpotify,
        bool HasDiscord,
        bool HasYouTube
    );

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
        string publicBaseUrl =
            !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(scheme)
                ? $"{scheme}://{host}"
                : (_config["App:BaseUrl"] ?? "http://localhost:5080").TrimEnd('/');

        // Issue a single-use bot-flow CSRF state nonce so the callback routes the setup-wizard bot auth
        // correctly and cannot be triggered by a forged state (§5).
        string state = await _oauthState.IssueAsync(new TwitchOAuthFlowState("bot"), ct);
        Result<string> url = await _authService.GetTwitchBotOAuthUrl(
            state,
            baseUrl: publicBaseUrl,
            cancellationToken: ct
        );
        if (url.IsFailure)
            return ResultResponse(url);
        return Ok(new StatusResponseDto<object> { Data = new { oauthUrl = url.Value } });
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
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTwitchCredentials(
        [FromBody] SaveTwitchCredentialRequest request,
        CancellationToken ct
    )
    {
        if (await IsSetupCompleteAsync(ct) && !User.IsInRole("admin"))
            return Forbid();

        await UpsertSystemConfig("twitch.client_id", request.ClientId, ct);
        await UpsertSystemConfig(
            "twitch.client_secret",
            request.ClientSecret,
            secure: true,
            ct: ct
        );
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
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSpotifyCredentials(
        [FromBody] SaveCredentialRequest request,
        CancellationToken ct
    )
    {
        if (await IsSetupCompleteAsync(ct) && !User.IsInRole("admin"))
            return Forbid();

        await UpsertSystemConfig("spotify.client_id", request.ClientId, ct);
        await UpsertSystemConfig(
            "spotify.client_secret",
            request.ClientSecret,
            secure: true,
            ct: ct
        );
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "Spotify credentials saved." });
    }

    /// <summary>Save system-level Discord app credentials.</summary>
    [HttpPut("setup/credentials/discord")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDiscordCredentials(
        [FromBody] SaveCredentialRequest request,
        CancellationToken ct
    )
    {
        if (await IsSetupCompleteAsync(ct) && !User.IsInRole("admin"))
            return Forbid();

        await UpsertSystemConfig("discord.client_id", request.ClientId, ct);
        await UpsertSystemConfig(
            "discord.client_secret",
            request.ClientSecret,
            secure: true,
            ct: ct
        );
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "Discord credentials saved." });
    }

    /// <summary>Save system-level YouTube (Google) app credentials.</summary>
    [HttpPut("setup/credentials/youtube")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveYouTubeCredentials(
        [FromBody] SaveCredentialRequest request,
        CancellationToken ct
    )
    {
        if (await IsSetupCompleteAsync(ct) && !User.IsInRole("admin"))
            return Forbid();

        await UpsertSystemConfig("youtube.client_id", request.ClientId, ct);
        await UpsertSystemConfig(
            "youtube.client_secret",
            request.ClientSecret,
            secure: true,
            ct: ct
        );
        await _db.SaveChangesAsync(ct);

        return Ok(new StatusResponseDto<object> { Message = "YouTube credentials saved." });
    }

    /// <summary>
    /// Finalize first-run setup. Once called — or once the system is otherwise ready — the credential
    /// endpoints lock to platform admins, so anonymous callers can no longer repoint the platform's app
    /// credentials. Callable anonymously only while setup is still open (the first-run window).
    /// </summary>
    [HttpPost("setup/complete")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteSetup(CancellationToken ct)
    {
        if (await IsSetupCompleteAsync(ct) && !User.IsInRole("admin"))
            return Forbid();

        await UpsertSystemConfig("system.setup_complete", "true", ct);
        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<object> { Message = "Setup marked complete." });
    }

    // Setup is "complete" once explicitly finalized, or once the system is ready (Twitch app + platform
    // bot both configured). After that, credential overwrites require a platform admin — closing the
    // standing hosted-mode hole where anyone could repoint the platform's Twitch app.
    private async Task<bool> IsSetupCompleteAsync(CancellationToken ct)
    {
        if (await GetSystemConfig("system.setup_complete", ct) == "true")
            return true;
        SetupState st = await ComputeSetupStateAsync(ct);
        return st.HasTwitch && st.HasPlatformBot;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // The credential reads (DB-vaulted, AAD-bound unprotect) live in ISystemCredentialsProvider — the one
    // resolution path the live OAuth flows also read from. The controller reads through it so there is a
    // single source of truth; the save endpoints below still own the write side.
    private Task<string?> GetSystemConfig(string key, CancellationToken ct) =>
        _credentials.GetValueAsync(KeyProvider(key), KeyField(key), ct);

    private async Task UpsertSystemConfig(
        string key,
        string value,
        CancellationToken ct,
        bool secure = false
    )
    {
        ConfigEntity? cfg = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == null && c.Key == key,
            ct
        );

        if (cfg is null)
        {
            cfg = new ConfigEntity { BroadcasterId = null, Key = key };
            _db.Configurations.Add(cfg);
        }

        // Secrets seal under the SAME AAD the provider opens them with ("system", provider, field), so the
        // value the wizard saves is the value the OAuth flows decrypt back.
        if (secure)
            cfg.SecureValue = await _protector.ProtectAsync(
                value,
                SystemCredentialsProviderContext(key),
                ct
            );
        else
            cfg.Value = value;
    }

    // The setup keys are "{provider}.{field}"; split them so the read delegates cleanly to the provider.
    private static string KeyProvider(string key)
    {
        int dot = key.IndexOf('.');
        return dot > 0 ? key[..dot] : "system";
    }

    private static string KeyField(string key)
    {
        int dot = key.IndexOf('.');
        return dot > 0 ? key[(dot + 1)..] : key;
    }

    // Reuse the provider's AAD shape so seal (here) and open (provider) agree exactly — one definition.
    private static TokenProtectionContext SystemCredentialsProviderContext(string key) =>
        NomNomzBot.Infrastructure.Platform.Configuration.SystemCredentialsProvider.ContextFor(key);
}
