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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Domain.Identity.Enums;

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
    // The streamer's own Twitch connection (self-host: the bot chats as the streamer's own account until a
    // dedicated bot registers) and the dedicated white-label bot connection (twitch_bot). Mirrors
    // AuthService's TwitchProvider / PlatformBotProvider so the scopes page reads exactly what the bot flow writes.
    private const string TwitchProvider = AuthEnums.IntegrationProvider.Twitch;
    private const string TwitchBotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";

    private readonly IAuthService _authService;
    private readonly IIntegrationTokenVault _vault;
    private readonly IConfiguration _config;
    private readonly ITwitchOAuthStateService _oauthState;

    public ChannelBotController(
        IAuthService authService,
        IIntegrationTokenVault vault,
        IConfiguration config,
        ITwitchOAuthStateService oauthState
    )
    {
        _authService = authService;
        _vault = vault;
        _config = config;
        _oauthState = oauthState;
    }

    private string GetPublicBaseUrl() => Request.ResolvePublicOrigin(_config);

    public record ScopeDto(
        string Scope,
        string Name,
        string Description,
        string Category,
        bool Granted,
        bool Required
    );

    public record ScopesResponseDto(List<ScopeDto> Permissions, int GrantedCount, int TotalCount);

    // The scopes the white-label bot OAuth flow actually grants (mirrors AuthService.BotScopes). This is the
    // BOT account's chat-permission page, not the streamer's — so it lists only the bot's chat scopes.
    //  - user:read:chat / user:write:chat are the live Helix chat path (read via EventSub, send via
    //    POST /helix/chat/messages) and are REQUIRED: their absence is a genuine "action required".
    //  - chat:read / chat:edit are the legacy IRC scopes. The bot OAuth still requests them, but the Helix
    //    chat path never exercises them (IRC is fully retired, scaling-qos.md §6). They are listed for
    //    transparency but are NOT required — so a self-host bot chatting as the streamer's own account
    //    (which holds the Helix scopes but not the IRC ones) still reads as complete.
    //  - user:read:whispers feeds the bot's whisper inbox (the platform-plane user.whisper.message topic).
    //    Not required: its absence 403s only that one topic, and no whisper-consuming surface exists yet —
    //    it flips to required when the bot-inbox feature ships.
    private static readonly (
        string Scope,
        string Name,
        string Description,
        string Category,
        bool Required
    )[] KnownScopes =
    [
        (
            "user:read:chat",
            "Read Chat",
            "Read chat messages so the bot can see and respond to commands",
            "Chat",
            true
        ),
        ("user:write:chat", "Send Chat Messages", "Send chat messages as the bot", "Chat", true),
        (
            "chat:read",
            "Read Chat (legacy IRC)",
            "Legacy IRC chat read — superseded by the Helix chat path and no longer used",
            "Chat",
            false
        ),
        (
            "chat:edit",
            "Send Chat (legacy IRC)",
            "Legacy IRC chat send — superseded by the Helix chat path and no longer used",
            "Chat",
            false
        ),
        (
            "user:read:whispers",
            "Read Whispers",
            "Receive whispers sent to the bot account (the bot's whisper inbox)",
            "Whispers",
            false
        ),
    ];

    /// <summary>
    /// Returns the white-label bot account's chat-permission status for this channel — the signal behind the
    /// dashboard's bot "action required" prompt. Reads the dedicated bot connection (<c>twitch_bot</c>, written
    /// by the bot OAuth flow), falling back to the streamer's own <c>twitch</c> connection for the self-host case
    /// where the bot chats as the streamer's own account until a custom bot is registered. Only the required
    /// scopes gate completion, so once the bot grant lands the prompt clears (100%).
    /// </summary>
    [HttpGet("{channelId}/scopes")]
    [Authorize]
    [RequireAction("channelbot:read")]
    [ProducesResponseType<StatusResponseDto<ScopesResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScopes(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<IReadOnlyList<IntegrationConnectionDto>> connections =
            await _vault.ListConnectionsAsync(tenantId, ct);
        IReadOnlyList<IntegrationConnectionDto> list = connections.IsSuccess
            ? connections.Value
            : [];

        // Prefer the dedicated white-label bot connection; fall back to the streamer's own connection for the
        // self-host case where the bot IS the streamer's own account (bot-identity self-host fallback).
        IntegrationConnectionDto? botConnection =
            list.FirstOrDefault(c => c.Provider == TwitchBotProvider)
            ?? list.FirstOrDefault(c => c.Provider == TwitchProvider);

        HashSet<string> grantedScopes =
            botConnection?.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<ScopeDto> permissions = KnownScopes
            .Select(s => new ScopeDto(
                s.Scope,
                s.Name,
                s.Description,
                s.Category,
                grantedScopes.Contains(s.Scope),
                s.Required
            ))
            .ToList();

        // Only the required scopes decide the "action required" prompt. The legacy IRC scopes are informational
        // (Required=false) and the Helix chat path never exercises them, so their absence must never keep the
        // prompt open — a completed bot grant (or a self-host streamer token carrying the Helix chat scopes)
        // reads 100%.
        int totalCount = permissions.Count(p => p.Required);
        int grantedCount = permissions.Count(p => p.Required && p.Granted);

        return Ok(
            new StatusResponseDto<ScopesResponseDto>
            {
                Data = new ScopesResponseDto(permissions, grantedCount, totalCount),
            }
        );
    }

    /// <summary>
    /// Start Twitch OAuth for this channel's white-label bot — an additional bot-identity registration on top
    /// of the owner's account (not a login), handled like an integration connect. Gated owner-level
    /// (identity-auth §5, <c>management/Broadcaster · channelbot:connect</c>). Returns the Twitch authorize URL
    /// (force_verify) for the client to open; the callback completes via the unified, nonce-validated
    /// <c>/auth/twitch/callback</c>.
    /// </summary>
    [HttpGet("{channelId}/bot/connect")]
    [Authorize]
    [RequireAction("channelbot:connect")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartChannelBotOAuth(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Issue a single-use, server-side CSRF state nonce holding the flow + tenant id, so the channel-bot
        // association cannot be forged by tampering with the state (§5). Only the opaque nonce leaves us.
        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("channel_bot", ChannelId: channelId),
            ct
        );

        Result<string> authUrl = await _authService.GetTwitchChannelBotOAuthUrl(
            tenantId,
            state,
            baseUrl: GetPublicBaseUrl(),
            cancellationToken: ct
        );
        if (authUrl.IsFailure)
            return ResultResponse(authUrl);
        return Ok(
            new StatusResponseDto<OAuthStartDto> { Data = new OAuthStartDto(authUrl.Value, state) }
        );
    }

    /// <summary>Get white-label bot status for a specific channel.</summary>
    [HttpGet("{channelId}/bot/status")]
    [Authorize]
    [RequireAction("channelbot:read")]
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
    [RequireAction("channelbot:disconnect")]
    public async Task<IActionResult> DisconnectChannelBot(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _authService.DisconnectChannelBotAsync(tenantId, ct);
        return ResultResponse(result);
    }
}
