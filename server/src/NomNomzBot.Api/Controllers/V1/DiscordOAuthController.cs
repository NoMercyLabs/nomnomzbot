// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The Discord bot-install OAuth flow (discord.md §5). Discord is not an ordinary user-resource provider (it
/// carries a guild authorization), so it is excluded from the generic descriptor-driven vaulted flow
/// (<see cref="IntegrationOAuthController"/>) by design (integrations-oauth §0). This controller owns the
/// <c>/connect</c> start + the anonymous callback; the callback parses the token response into a
/// <see cref="DiscordGuildOAuthResult"/> and delegates ALL persistence to <see cref="IDiscordGuildService"/>,
/// which vaults the bot token through <c>IIntegrationTokenVault</c> (no plaintext) and records the both-opt-in
/// guild link. No persistence is done inline here.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Tags("Integration OAuth")]
public class DiscordOAuthController : BaseController
{
    private const string DiscordScopes = "bot guilds";

    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscordOAuthController> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IDiscordOAuthStateService _oauthState;
    private readonly IDiscordGuildService _discord;

    public DiscordOAuthController(
        IApplicationDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<DiscordOAuthController> logger,
        TimeProvider timeProvider,
        IDiscordOAuthStateService oauthState,
        IDiscordGuildService discord
    )
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeProvider = timeProvider;
        _oauthState = oauthState;
        _discord = discord;
    }

    /// <summary>
    /// Start the Discord OAuth flow — redirects to Discord's authorization page with bot + guilds scopes.
    /// Pass <paramref name="redirect_uri"/> for the desktop client's loopback listener (RFC-8252): the callback
    /// then redirects there with a success/error marker so the listener completes. The web/SPA client omits it
    /// and gets the frontend redirect instead.
    /// </summary>
    [HttpGet("channels/{channelId}/integrations/discord/callback/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartDiscordOAuth(
        string channelId,
        [FromQuery] string? redirect_uri,
        CancellationToken ct
    )
    {
        // Bound the post-auth redirect to the open-redirect policy (blank / app scheme / loopback only) so a
        // crafted link cannot bounce the connect result to an attacker-controlled host.
        if (!ClientRedirectPolicy.IsAllowed(redirect_uri))
            return BadRequestResponse("Disallowed redirect_uri.");

        string? clientId = await GetConfigValueAsync("discord.client_id", ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequestResponse(
                "Discord client ID is not configured. Add a Configuration row with Key='discord.client_id'."
            );

        string baseUrl = Request.ResolvePublicOrigin(_config);
        string redirectUri = $"{baseUrl}/api/v1/integrations/discord/callback";

        // Single-use, server-side CSRF state nonce (the channel id + optional loopback redirect are held
        // server-side, never in the query string) so a forged callback cannot bind a Discord guild to a
        // channel the caller did not choose, nor bounce the result to an unvetted target.
        string state = await _oauthState.IssueAsync(
            new DiscordOAuthFlowState(channelId, redirect_uri),
            ct
        );

        string authUrl =
            "https://discord.com/api/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + "&response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString(DiscordScopes)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the Discord OAuth callback. Exchanges the authorization code for tokens, parses the response into
    /// a <see cref="DiscordGuildOAuthResult"/>, and delegates persistence to <see cref="IDiscordGuildService"/>
    /// (which vaults the bot token and records the guild link). No persistence is done inline.
    /// </summary>
    [HttpGet("integrations/discord/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleDiscordCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct
    )
    {
        // Consume the single-use nonce; a missing, expired, or forged state is rejected, and the channel id +
        // optional loopback redirect come only from the server-side payload, never the query string.
        DiscordOAuthFlowState? flowState = await _oauthState.ConsumeAsync(state, ct);
        string? channelId = flowState?.ChannelId;
        string? loopbackRedirect = flowState?.RedirectUri;
        if (string.IsNullOrEmpty(channelId))
            return BadRequestResponse("Invalid or expired OAuth state.");

        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel_id in OAuth state.");

        string? clientId = await GetConfigValueAsync("discord.client_id", ct);
        string? clientSecret = await GetConfigSecureValueAsync("discord.client_secret", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return FailureResult(
                loopbackRedirect,
                "credentials_unconfigured",
                () => BadRequestResponse("Discord client credentials are not configured.")
            );

        // The token exchange's redirect_uri must match, byte for byte, the one sent at /start. Discord redirects
        // back through the same proxy/tunnel, so the same shared resolver yields the same public origin here.
        string baseUrl = Request.ResolvePublicOrigin(_config);
        string redirectUri = $"{baseUrl}/api/v1/integrations/discord/callback";

        using HttpClient client = _httpClientFactory.CreateClient();

        using FormUrlEncodedContent tokenRequest = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }
        );

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(
                "https://discord.com/api/oauth2/token",
                tokenRequest,
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange Discord authorization code");
            return FailureResult(
                loopbackRedirect,
                "token_endpoint_unreachable",
                () => InternalServerErrorResponse("Failed to contact Discord token endpoint.")
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Discord token exchange failed: {Status} {Body}",
                response.StatusCode,
                errorBody
            );
            return FailureResult(
                loopbackRedirect,
                "token_exchange_failed",
                () => InternalServerErrorResponse("Discord token exchange failed.")
            );
        }

        using JsonDocument tokenDoc = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(ct)
        );
        JsonElement root = tokenDoc.RootElement;

        string? accessToken = root.TryGetProperty("access_token", out JsonElement atProp)
            ? atProp.GetString()
            : null;
        if (string.IsNullOrEmpty(accessToken))
            return FailureResult(
                loopbackRedirect,
                "no_access_token",
                () => BadRequestResponse("Discord did not return an access token.")
            );

        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement rtProp)
            ? rtProp.GetString()
            : null;
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement expProp)
            ? expProp.GetInt32()
            : 604800;
        string? guildId =
            root.TryGetProperty("guild", out JsonElement guildProp)
            && guildProp.TryGetProperty("id", out JsonElement guildIdProp)
                ? guildIdProp.GetString()
                : null;
        string? guildName =
            root.TryGetProperty("guild", out JsonElement guildProp2)
            && guildProp2.TryGetProperty("name", out JsonElement guildNameProp)
                ? guildNameProp.GetString()
                : null;

        if (string.IsNullOrEmpty(guildId))
            return FailureResult(
                loopbackRedirect,
                "no_guild",
                () =>
                    BadRequestResponse(
                        "Discord did not return a guild — the bot was not installed."
                    )
            );

        // Parse the token response into the spec's DiscordGuildOAuthResult and hand ALL persistence to the
        // guild service: it vaults the bot token (no plaintext) and records the both-opt-in guild link.
        DiscordGuildOAuthResult oauth = new(
            guildId,
            guildName,
            accessToken,
            refreshToken,
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(expiresIn),
            DiscordScopes.Split(' '),
            InstalledByDiscordUserId: null
        );

        Result<DiscordGuildConnectionDto> upsert = await _discord.UpsertFromOAuthAsync(
            tenantId,
            oauth,
            ct
        );
        if (upsert.IsFailure)
        {
            _logger.LogError(
                "Discord guild link failed for channel {ChannelId}: {Error}",
                channelId,
                upsert.ErrorMessage
            );
            return FailureResult(
                loopbackRedirect,
                "guild_link_failed",
                () => InternalServerErrorResponse("Failed to record the Discord guild link.")
            );
        }

        _logger.LogInformation(
            "Discord OAuth completed for channel {ChannelId}, guild {GuildId}",
            channelId,
            guildId
        );

        // Desktop client: bounce back to its loopback listener with the success marker so the connect completes.
        if (!string.IsNullOrWhiteSpace(loopbackRedirect))
            return Redirect(AppendQuery(loopbackRedirect, "discord_connected=true"));

        // Web/SPA client: no loopback listener — land on the integrations screen.
        string frontendUrl =
            _config["App:FrontendUrl"]
            ?? _config["App:BaseUrl"]
            ?? $"{Request.Scheme}://{Request.Host}";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?discord_connected=true");
    }

    /// <summary>
    /// Routes a callback failure: when a desktop loopback <c>redirect_uri</c> was supplied (already vetted by
    /// <see cref="ClientRedirectPolicy"/> at start), bounce there with <c>?error=&lt;reason&gt;</c> so the
    /// loopback listener completes (failure path); otherwise return the supplied web/API error response.
    /// </summary>
    private IActionResult FailureResult(
        string? loopbackRedirect,
        string reason,
        Func<IActionResult> webResponse
    ) =>
        string.IsNullOrWhiteSpace(loopbackRedirect)
            ? webResponse()
            : Redirect(AppendQuery(loopbackRedirect, $"error={Uri.EscapeDataString(reason)}"));

    /// <summary>Appends <paramref name="query"/> to <paramref name="url"/>, choosing <c>?</c> or <c>&amp;</c>.</summary>
    private static string AppendQuery(string url, string query) =>
        url + (url.Contains('?') ? '&' : '?') + query;

    /// <summary>Read a plain-text Configuration value (platform-scoped) from the database, falling back to config/env.</summary>
    private async Task<string?> GetConfigValueAsync(string key, CancellationToken ct)
    {
        string? dbValue = await _db
            .Configurations.Where(c => c.BroadcasterId == null && c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(dbValue))
            return dbValue;

        return _config[ToPascalConfigKey(key)]
            ?? Environment.GetEnvironmentVariable(ToPascalEnvKey(key));
    }

    /// <summary>Read a secure (encrypted) Configuration value (platform-scoped) from the database, falling back to config/env.</summary>
    private async Task<string?> GetConfigSecureValueAsync(string key, CancellationToken ct)
    {
        string? dbValue = await _db
            .Configurations.Where(c => c.BroadcasterId == null && c.Key == key)
            .Select(c => c.SecureValue)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(dbValue))
            return dbValue;

        return _config[ToPascalConfigKey(key)]
            ?? Environment.GetEnvironmentVariable(ToPascalEnvKey(key));
    }

    /// <summary>Convert "discord.client_id" → "Discord:ClientId" for IConfiguration.</summary>
    private static string ToPascalConfigKey(string key) =>
        string.Join(":", key.Split('.').Select(ToPascalCase));

    /// <summary>Convert "discord.client_id" → "Discord__ClientId" for environment variables.</summary>
    private static string ToPascalEnvKey(string key) =>
        string.Join("__", key.Split('.').Select(ToPascalCase));

    private static string ToPascalCase(string segment) =>
        string.Concat(
            segment.Split('_').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w)
        );
}
