// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Music;

/// <summary>
/// Bridges a just-connected music-provider OAuth grant (Spotify / YouTube) into the legacy
/// <c>Service</c> token store that the music providers (<c>SpotifyMusicProvider</c> /
/// <c>YouTubeMusicProvider</c>) and <c>IntegrationsController.ListIntegrations</c> still read from.
/// <para>
/// The integration connect flow vaults tokens in the crypto vault (<c>IIntegrationTokenVault</c>), which is
/// the canonical store — but nothing writes the <c>Service</c> row those consumers query, so playback/search
/// treat the provider as disconnected and the dashboard loops on "reconnect". This mirror closes that gap:
/// after a connect it upserts the matching <c>Service</c> row (idempotent), sealing the tokens + client
/// credentials in the exact <c>TokenProtectionContext</c> shape the providers unseal, so their own
/// refresh-on-demand + rotation path keeps working from there.
/// </para>
/// It is a mirror, not a replacement: the vault write stays canonical. A non-music provider is a no-op.
/// </summary>
public interface IMusicProviderTokenMirror
{
    /// <summary>
    /// Upserts the music-provider <c>Service</c> row for <paramref name="broadcasterId"/> from the tokens +
    /// client credentials a connect just produced. No-op when <paramref name="provider"/> is not a music
    /// provider. Idempotent: an existing row is updated in place.
    /// </summary>
    Task MirrorAsync(
        Guid broadcasterId,
        string provider,
        string accessToken,
        string? refreshToken,
        DateTime? tokenExpiry,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default
    );
}
