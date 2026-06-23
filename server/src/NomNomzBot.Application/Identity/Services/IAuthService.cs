// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Twitch identity/login + bot OAuth (identity-auth §3.1). Ids are <see cref="Guid"/>; the callback is
/// session-aware (opens an <c>AuthSession</c> + issues a rotating refresh token via <c>ISessionService</c>)
/// and vaults Twitch tokens via <c>IIntegrationTokenVault</c>. Twitch is the identity/login provider; the
/// generic non-Twitch connect lives in <c>IIntegrationOAuthService</c>.
/// </summary>
public interface IAuthService
{
    // ── User OAuth ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the Twitch login authorize URL, resolving the platform's Twitch client id from the
    /// configured app credentials (DB-vaulted first, then config). Fails <c>TWITCH_NOT_CONFIGURED</c> when no
    /// credentials are set yet — so the dashboard tells the operator to finish setup instead of starting a
    /// broken OAuth flow.
    /// </summary>
    Task<Result<string>> GetTwitchOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(
        OAuthCallbackDto callback,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );

    Task<Result<AuthResultDto>> RefreshTokenAsync(
        string refreshToken,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );

    Task<Result> LogoutAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    );

    Task<Result<int>> LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default);

    // ── Platform bot (NomNomzBot) — IsPlatformPrincipal gate, BroadcasterId=null ──

    /// <summary>
    /// Builds the bot-account authorize URL. Fails <c>TWITCH_NOT_CONFIGURED</c> until the Twitch app
    /// credentials are configured (the wizard's first step), so the bot step can't run before its prerequisite.
    /// </summary>
    Task<Result<string>> GetTwitchBotOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    );

    Task<Result<BotStatusDto>> GetBotStatusAsync(CancellationToken cancellationToken = default);

    Task<Result> DisconnectBotAsync(CancellationToken cancellationToken = default);

    // ── Custom (white-label) bot — per-channel, BroadcasterId=channelId ──

    /// <summary>
    /// Builds the per-channel custom-bot authorize URL. Fails <c>TWITCH_NOT_CONFIGURED</c> until the Twitch
    /// app credentials are configured.
    /// </summary>
    Task<Result<string>> GetTwitchChannelBotOAuthUrl(
        Guid broadcasterId,
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(
        Guid broadcasterId,
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    );

    Task<Result<BotStatusDto>> GetChannelBotStatusAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result> DisconnectChannelBotAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>DTO describing the connected bot account.</summary>
public record BotStatusDto(
    bool Connected,
    string? Login,
    string? DisplayName,
    string? ProfileImageUrl
);
