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

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// Manages a channel's own Spotify app credentials (BYOC) — the dashboard-facing write/read/clear surface
/// over the channel-scoped <c>Configuration</c> rows <see cref="Common.Interfaces.IChannelCredentialsResolver"/>
/// resolves. The secret is sealed at rest under the token-protector AAD and is never echoed back in plaintext.
/// </summary>
public interface IChannelSpotifyCredentialsService
{
    /// <summary>The channel's stored Spotify credential state: the client id (safe to show) and whether a
    /// secret is configured — never the secret itself.</summary>
    Task<Result<ChannelSpotifyCredentialsDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Stores the channel's own Spotify client id + secret, sealed at rest. Both fields are required —
    /// a partial credential can never silently shadow the app-level fallback.</summary>
    Task<Result<ChannelSpotifyCredentialsDto>> SetAsync(
        Guid channelId,
        SetChannelSpotifyCredentialsDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the channel's own Spotify credentials so Spotify OAuth falls back to the app-level
    /// configuration.</summary>
    Task<Result<ChannelSpotifyCredentialsDto>> ClearAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Read-model for a channel's Spotify BYOC state — never carries the secret.</summary>
public sealed record ChannelSpotifyCredentialsDto(string? ClientId, bool HasClientSecret);

/// <summary>Request body to store a channel's own Spotify app credentials.</summary>
public sealed record SetChannelSpotifyCredentialsDto(string ClientId, string ClientSecret);
