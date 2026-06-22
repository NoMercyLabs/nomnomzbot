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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// INTERIM Discord connect flow. Discord's real home is the bespoke guild/bot subsystem in <c>discord.md</c>
/// (the both-opt-in handshake + encrypted bot-token storage owned by <c>IDiscordGuildService</c>), which is not
/// built yet. Until it lands, this controller preserves the existing Discord OAuth behaviour unchanged so the
/// integration does not regress — but it deliberately does NOT use the generic vaulted flow
/// (<see cref="IntegrationOAuthController"/>): Discord is not an ordinary user-resource provider (it carries a
/// guild authorization), so it is excluded from the descriptor-driven path by design (integrations-oauth §0).
/// <para>
/// KNOWN INTERIM DEBT (resolved when the discord.md subsystem is built): tokens land in the legacy
/// <c>Service</c> entity in plaintext rather than the crypto vault. (The OAuth <c>state</c> is now a single-use
/// server-side CSRF nonce via <c>ITwitchOAuthStateService</c>, not an unsigned base64 payload.) Do not extend
/// this controller — fold it into <c>IDiscordGuildService</c> instead.
/// </para>
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
    private readonly ITwitchOAuthStateService _oauthState;

    public DiscordOAuthController(
        IApplicationDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<DiscordOAuthController> logger,
        TimeProvider timeProvider,
        ITwitchOAuthStateService oauthState
    )
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeProvider = timeProvider;
        _oauthState = oauthState;
    }

    /// <summary>Start the Discord OAuth flow — redirects to Discord's authorization page with bot + guilds scopes.</summary>
    [HttpGet("channels/{channelId}/integrations/discord/callback/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartDiscordOAuth(string channelId, CancellationToken ct)
    {
        string? clientId = await GetConfigValueAsync("discord.client_id", ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequestResponse(
                "Discord client ID is not configured. Add a Configuration row with Key='discord.client_id'."
            );

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string redirectUri = $"{baseUrl}/api/v1/integrations/discord/callback";

        // Single-use, server-side CSRF state nonce (the channel id is held server-side, never in the query
        // string) so a forged callback cannot bind a Discord guild to a channel the caller did not choose.
        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("discord", ChannelId: channelId),
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
    /// Handle the Discord OAuth callback. Exchanges the authorization code for tokens, stores them, and records
    /// the guild authorization. (discord.md will move persistence behind <c>IDiscordGuildService</c>.)
    /// </summary>
    [HttpGet("integrations/discord/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleDiscordCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct
    )
    {
        // Consume the single-use nonce; a missing, expired, or forged state is rejected, and the channel id
        // comes only from the server-side payload, never the query string.
        TwitchOAuthFlowState? flowState = await _oauthState.ConsumeAsync(state, ct);
        string? channelId = flowState?.ChannelId;
        if (string.IsNullOrEmpty(channelId))
            return BadRequestResponse("Invalid or expired OAuth state.");

        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel_id in OAuth state.");

        string? clientId = await GetConfigValueAsync("discord.client_id", ct);
        string? clientSecret = await GetConfigSecureValueAsync("discord.client_secret", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return BadRequestResponse("Discord client credentials are not configured.");

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
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
            return InternalServerErrorResponse("Failed to contact Discord token endpoint.");
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Discord token exchange failed: {Status} {Body}",
                response.StatusCode,
                errorBody
            );
            return InternalServerErrorResponse("Discord token exchange failed.");
        }

        using JsonDocument tokenDoc = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(ct)
        );
        JsonElement root = tokenDoc.RootElement;

        string? accessToken = root.GetProperty("access_token").GetString();
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

        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "discord" && s.BroadcasterId == tenantId,
            ct
        );

        if (service is null)
        {
            service = new Service
            {
                Name = "discord",
                BroadcasterId = tenantId,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = accessToken;
        service.RefreshToken = refreshToken;
        service.TokenExpiry = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(expiresIn);
        service.Scopes = DiscordScopes.Split(' ');
        service.Enabled = true;

        if (!string.IsNullOrEmpty(guildId))
        {
            DiscordServerAuthorization? discordAuth =
                await _db.DiscordServerAuthorizations.FirstOrDefaultAsync(
                    d => d.BroadcasterId == tenantId && d.GuildId == guildId,
                    ct
                );

            if (discordAuth is null)
            {
                discordAuth = new DiscordServerAuthorization
                {
                    BroadcasterId = tenantId,
                    GuildId = guildId,
                    GuildName = guildName ?? "Unknown",
                    Status = "active",
                    ApprovedAt = _timeProvider.GetUtcNow().UtcDateTime,
                };
                _db.DiscordServerAuthorizations.Add(discordAuth);
            }
            else
            {
                discordAuth.GuildName = guildName ?? discordAuth.GuildName;
                discordAuth.Status = "active";
                discordAuth.ApprovedAt = _timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Discord OAuth completed for channel {ChannelId}, guild {GuildId}",
            channelId,
            guildId
        );

        string frontendUrl =
            _config["App:FrontendUrl"]
            ?? _config["App:BaseUrl"]
            ?? $"{Request.Scheme}://{Request.Host}";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?discord_connected=true");
    }

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
