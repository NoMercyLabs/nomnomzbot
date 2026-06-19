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
/// Application service for authentication: OAuth callbacks, token refresh, and logout.
/// </summary>
public interface IAuthService
{
    /// <summary>Get the Twitch OAuth authorization URL.</summary>
    Task<string> GetTwitchOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Handle the OAuth callback from Twitch and return auth tokens.</summary>
    Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    );

    /// <summary>Refresh an expired access token.</summary>
    Task<Result<AuthResultDto>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Log out a user, revoking their tokens.</summary>
    Task<Result> LogoutAsync(string userId, CancellationToken cancellationToken = default);

    // ── Platform bot (NomNomzBot) — admin only, BroadcasterId=null ──────────

    /// <summary>Get the Twitch OAuth URL for the platform bot account (NomNomzBot).</summary>
    Task<string> GetTwitchBotOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Handle the OAuth callback for the platform bot and store the token globally.</summary>
    Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the platform bot connection status.</summary>
    Task<Result<BotStatusDto>> GetBotStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnect the platform bot.</summary>
    Task<Result> DisconnectBotAsync(CancellationToken cancellationToken = default);

    // ── White-label bot — per-channel, Pro tier, BroadcasterId=channelId ─────

    /// <summary>Get the Twitch OAuth URL for a channel's white-label bot.</summary>
    Task<string> GetTwitchChannelBotOAuthUrl(
        string channelId,
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Handle the OAuth callback for a channel's white-label bot and store per-channel.</summary>
    Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(
        string channelId,
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the white-label bot status for a specific channel.</summary>
    Task<Result<BotStatusDto>> GetChannelBotStatusAsync(
        string channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Disconnect the white-label bot for a specific channel.</summary>
    Task<Result> DisconnectChannelBotAsync(
        string channelId,
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
