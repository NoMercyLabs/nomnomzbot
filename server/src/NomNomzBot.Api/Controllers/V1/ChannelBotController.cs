// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Manages white-label (per-channel) bot accounts.
/// A white-label bot is a separate Twitch account the channel owner authenticates so
/// that bot messages appear from their own bot identity rather than the platform bot (NomNomzBot).
///
/// Token is stored as Service(Name="twitch_bot", BroadcasterId=channelId).
/// The platform bot (BroadcasterId=null) is managed in AdminController.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Tags("Channel Bot")]
public class ChannelBotController : BaseController
{
    private readonly IAuthService _authService;
    private readonly IIntegrationTokenVault _vault;
    private readonly IConfiguration _config;

    public ChannelBotController(
        IAuthService authService,
        IIntegrationTokenVault vault,
        IConfiguration config
    )
    {
        _authService = authService;
        _vault = vault;
        _config = config;
    }

    private string GetPublicBaseUrl()
    {
        string forwardedHost = Request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();

        string? host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : Request.Host.Value;

        string? scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : Request.Scheme;

        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(scheme))
            return $"{scheme}://{host}";

        return (_config["App:BaseUrl"] ?? "http://localhost:5080").TrimEnd('/');
    }

    public record ScopeDto(
        string Scope,
        string Name,
        string Description,
        string Category,
        bool Granted,
        bool Required
    );

    public record ScopesResponseDto(List<ScopeDto> Permissions, int GrantedCount, int TotalCount);

    private static readonly (
        string Scope,
        string Name,
        string Description,
        string Category,
        bool Required
    )[] KnownScopes =
    [
        ("user:read:email", "Read Email", "Access your verified email address", "Account", true),
        ("user:read:chat", "Read Chat (user)", "Read chat messages as you", "Chat", true),
        ("chat:read", "Read Chat", "Read live stream chat and rooms", "Chat", true),
        (
            "chat:edit",
            "Send Chat Messages",
            "Send live stream chat and rooms messages",
            "Chat",
            true
        ),
        (
            "channel:read:subscriptions",
            "Read Subscriptions",
            "View your channel's subscription events",
            "Channel",
            true
        ),
        ("bits:read", "Read Bits", "View Bits information for your channel", "Channel", true),
        (
            "channel:manage:redemptions",
            "Manage Redemptions",
            "Manage channel point redemption statuses",
            "Rewards",
            true
        ),
        (
            "channel:read:redemptions",
            "Read Redemptions",
            "View channel point custom reward redemptions",
            "Rewards",
            true
        ),
        (
            "moderator:read:chatters",
            "Read Chatters",
            "View the list of chatters in your channel",
            "Moderation",
            true
        ),
        (
            "moderator:manage:banned_users",
            "Manage Bans",
            "Ban and unban users in your channel",
            "Moderation",
            true
        ),
        (
            "moderator:manage:chat_messages",
            "Delete Messages",
            "Delete chat messages in your channel",
            "Moderation",
            true
        ),
        (
            "moderator:manage:chat_settings",
            "Manage Chat Settings",
            "Update chat settings such as slow mode and subscriber-only",
            "Moderation",
            true
        ),
        (
            "moderator:read:followers",
            "Read Followers",
            "Read information about followers in your channel",
            "Channel",
            true
        ),
        (
            "channel:moderate",
            "Channel Moderate",
            "Perform moderation actions in your channel",
            "Moderation",
            true
        ),
        (
            "channel:manage:broadcast",
            "Manage Broadcast",
            "Update your channel's title, game, and other settings",
            "Stream",
            true
        ),
        (
            "channel:read:polls",
            "Read Polls",
            "View information about polls in your channel",
            "Polls",
            true
        ),
        (
            "channel:manage:polls",
            "Manage Polls",
            "Create and end polls in your channel",
            "Polls",
            true
        ),
        (
            "channel:read:predictions",
            "Read Predictions",
            "View information about predictions in your channel",
            "Predictions",
            true
        ),
        (
            "channel:manage:predictions",
            "Manage Predictions",
            "Create and end predictions in your channel",
            "Predictions",
            true
        ),
        ("channel:read:vips", "Read VIPs", "View your channel's VIP list", "Channel", true),
    ];

    /// <summary>Returns OAuth scopes status for the broadcaster token on this channel.</summary>
    [HttpGet("{channelId}/scopes")]
    [Authorize]
    [ProducesResponseType<StatusResponseDto<ScopesResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScopes(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<IReadOnlyList<IntegrationConnectionDto>> connections =
            await _vault.ListConnectionsAsync(tenantId, ct);
        IntegrationConnectionDto? twitch = connections.IsSuccess
            ? connections.Value.FirstOrDefault(c => c.Provider == "twitch")
            : null;

        var grantedScopes =
            twitch?.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var permissions = KnownScopes
            .Select(s => new ScopeDto(
                s.Scope,
                s.Name,
                s.Description,
                s.Category,
                grantedScopes.Contains(s.Scope),
                s.Required
            ))
            .ToList();

        int grantedCount = permissions.Count(p => p.Granted);

        return Ok(
            new StatusResponseDto<ScopesResponseDto>
            {
                Data = new ScopesResponseDto(permissions, grantedCount, permissions.Count),
            }
        );
    }

    /// <summary>
    /// Start Twitch OAuth for this channel's white-label bot.
    /// Opens Twitch with force_verify=true so the streamer can log in as their bot account.
    /// </summary>
    [HttpGet("{channelId}/bot/connect")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartChannelBotOAuth(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Embed the flow + tenant id so the single callback endpoint can route it back.
        string state = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { flow = "channel_bot", channel_id = channelId })
            )
        );

        string authUrl = await _authService.GetTwitchChannelBotOAuthUrl(
            tenantId,
            state,
            baseUrl: GetPublicBaseUrl(),
            cancellationToken: ct
        );
        return Redirect(authUrl);
    }

    /// <summary>
    /// Twitch OAuth callback for the white-label bot.
    /// Reads channelId from the state parameter embedded during StartChannelBotOAuth.
    /// Stores the token as Service(Name="twitch_bot", BroadcasterId=channelId).
    /// </summary>
    [HttpGet("callback/bot")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> HandleChannelBotCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct
    )
    {
        string? channelId = null;
        string? mobileRedirectUri = null;

        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(state);
                using var doc = JsonDocument.Parse(decoded);
                if (doc.RootElement.TryGetProperty("channel_id", out var cidEl))
                    channelId = cidEl.GetString();
                if (doc.RootElement.TryGetProperty("redirect_uri", out var uriEl))
                    mobileRedirectUri = uriEl.GetString();
            }
            catch { }
        }

        if (string.IsNullOrEmpty(channelId) || !Guid.TryParse(channelId, out Guid tenantId))
            return BadRequest("Missing or invalid channel_id in state.");

        string callbackUri = $"{GetPublicBaseUrl()}/api/v1/channels/callback/bot";
        Result<BotStatusDto> result = await _authService.HandleTwitchChannelBotCallbackAsync(
            tenantId,
            new OAuthCallbackDto { Code = code, RedirectUri = callbackUri },
            ct
        );

        if (result.IsFailure)
        {
            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                return Redirect($"{mobileRedirectUri}?error=bot_auth_failed");
            return ResultResponse(result);
        }

        // Redirect back to integrations page
        if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
            return Redirect($"{mobileRedirectUri}?custom_bot_connected=true");

        string frontendUrl = _config["App:FrontendUrl"] ?? "https://bot-dev.nomercy.tv";
        return Redirect($"{frontendUrl}/(dashboard)/integrations?custom_bot_connected=true");
    }

    /// <summary>Get white-label bot status for a specific channel.</summary>
    [HttpGet("{channelId}/bot/status")]
    [Authorize]
    public async Task<IActionResult> GetChannelBotStatus(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<BotStatusDto> result = await _authService.GetChannelBotStatusAsync(tenantId, ct);
        return ResultResponse(result);
    }

    /// <summary>Disconnect the white-label bot for a specific channel.</summary>
    [HttpDelete("{channelId}/bot")]
    [Authorize]
    public async Task<IActionResult> DisconnectChannelBot(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _authService.DisconnectChannelBotAsync(tenantId, ct);
        return ResultResponse(result);
    }
}
