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

    /// <summary>
    /// One readiness check. <see cref="Ready"/> means the area is <em>usable now</em>; <see cref="Ok"/> means the
    /// <em>full</em> credential set is present. For Twitch the two diverge: a client id alone makes the bot fully
    /// functional via the secret-free Device Code Flow (<see cref="Ready"/> = true), while a client secret is the
    /// pure enhancement that <em>also</em> unlocks the one-tap redirect sign-in (<see cref="Ok"/> = true). For
    /// every other area the two coincide. The frontend routes onboarding off these: <see cref="Ready"/> gates
    /// whether the flow can start at all; <see cref="Ok"/> picks the smoother redirect login over device-code.
    /// </summary>
    public record CheckItem(bool Ok, bool Ready, string Status, string? Detail);

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
        bool hasTwitchClientId = st.HasTwitchClientId;
        bool hasTwitchSecret = st.HasTwitchSecret;
        bool hasPlatformBot = st.HasPlatformBot;
        bool hasSpotify = st.HasSpotify;
        bool hasDiscord = st.HasDiscord;
        bool hasYouTube = st.HasYouTube;

        // The system is ready once Twitch is USABLE (a client id — the bot logs in and talks via the secret-free
        // Device Code Flow) and the platform bot is authorized. A client secret is NOT required for readiness; it
        // only adds the one-tap redirect sign-in. A missing client id is still not-ready; a missing secret is not.
        bool ready = hasTwitchClientId && hasPlatformBot;

        SystemChecks checks = new SystemChecks(
            // Ok = redirect-capable (secret present); Ready = usable now (client id present → device-code works).
            TwitchApp: new CheckItem(
                Ok: hasTwitchSecret,
                Ready: hasTwitchClientId,
                Status: !hasTwitchClientId ? "missing"
                    : hasTwitchSecret ? "ready_redirect"
                    : "ready_device",
                Detail: !hasTwitchClientId
                        ? (
                            _env.IsDevelopment()
                                ? "Set TWITCH_CLIENT_ID in .env (a client secret is optional)"
                                : "Not configured"
                        )
                    : hasTwitchSecret
                        ? "Client ID and secret are set — one-tap redirect sign-in is available"
                    : "Ready via the secret-free device-code flow — add a client secret to also enable one-tap redirect sign-in"
            ),
            PlatformBot: new CheckItem(
                Ok: hasPlatformBot,
                Ready: hasPlatformBot,
                Status: hasPlatformBot ? "connected" : "disconnected",
                Detail: hasPlatformBot
                    ? "Bot account is authorized"
                    : "Authorize the bot's Twitch account"
            ),
            Spotify: new CheckItem(
                Ok: hasSpotify,
                Ready: hasSpotify,
                Status: hasSpotify ? "configured" : "not configured",
                Detail: hasSpotify
                    ? "Client ID and secret are set"
                    : "Optional — configure to enable song requests"
            ),
            Discord: new CheckItem(
                Ok: hasDiscord,
                Ready: hasDiscord,
                Status: hasDiscord ? "configured" : "not configured",
                Detail: hasDiscord
                    ? "Client ID and secret are set"
                    : "Optional — configure to enable Discord integration"
            ),
            YouTube: new CheckItem(
                Ok: hasYouTube,
                Ready: hasYouTube,
                Status: hasYouTube ? "configured" : "not configured",
                Detail: hasYouTube
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
                    // The Twitch step is complete once a client id is present — a secret is optional, so id-only
                    // is a finished, fully-functional configuration (device-code login). Without a secret the
                    // step stays complete but the status flags the redirect enhancement as still available.
                    st.HasTwitchClientId,
                    st.HasTwitchSecret,
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
        // Two Twitch facts the onboarding hinges on, kept distinct because a secret is OPTIONAL:
        //   • a client id alone makes the bot fully functional via the secret-free Device Code Flow, so it is
        //     what "Twitch is ready" actually means (the shipped public client or a BYOC override);
        //   • a client SECRET (both fields present) is the pure enhancement that additionally unlocks the
        //     one-tap redirect sign-in. GetAsync returns non-null only when BOTH fields resolve.
        bool hasTwitchClientId = !string.IsNullOrWhiteSpace(
            await _credentials.GetClientIdAsync("twitch", ct)
        );
        bool hasTwitchSecret = await _credentials.GetAsync("twitch", ct) is not null;

        // The optional providers stay "configured = both fields" — they have no device-code path, so a secret
        // is genuinely required to use them at all.
        bool hasSpotify = await _credentials.GetAsync("spotify", ct) is not null;
        bool hasDiscord = await _credentials.GetAsync("discord", ct) is not null;
        bool hasYouTube = await _credentials.GetAsync("youtube", ct) is not null;

        // The platform bot is "configured" exactly when its account is authorized and the vaulted token still
        // decrypts — the same fact the bot's Twitch calls depend on. GetBotStatusAsync reads the canonical
        // BotAccount + token vault (the rebuild's source of truth), not the retired flat Service table.
        Result<BotStatusDto> botStatus = await _authService.GetBotStatusAsync(ct);
        bool hasPlatformBot = botStatus is { IsSuccess: true, Value.Connected: true };

        return new SetupState(
            hasTwitchClientId,
            hasTwitchSecret,
            hasPlatformBot,
            hasSpotify,
            hasDiscord,
            hasYouTube
        );
    }

    private sealed record SetupState(
        bool HasTwitchClientId,
        bool HasTwitchSecret,
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

    // Setup is "complete" once explicitly finalized, or once the system is ready (a Twitch client id — the
    // secret is optional — plus the platform bot authorized). After that, credential overwrites require a
    // platform admin — closing the standing hosted-mode hole where anyone could repoint the platform's app.
    private async Task<bool> IsSetupCompleteAsync(CancellationToken ct)
    {
        if (await GetSystemConfig("system.setup_complete", ct) == "true")
            return true;
        SetupState st = await ComputeSetupStateAsync(ct);
        return st.HasTwitchClientId && st.HasPlatformBot;
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
