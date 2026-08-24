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

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// BYOC resolution for a channel's OWN OAuth app credentials (a streamer's own Spotify/Discord/YouTube app),
/// layered on top of <see cref="ISystemCredentialsProvider"/> exactly the way that provider layers the
/// wizard-vaulted system credentials over env/appsettings: a channel-scoped <c>Configuration</c> row
/// (<c>BroadcasterId == channelId</c>, <c>Key = "{provider}.client_id"</c> plain / <c>"{provider}.client_secret"</c>
/// sealed) wins when BOTH fields are present; otherwise the resolution falls through to the platform's
/// system-level app credentials. Every live OAuth path (authorize URL, code-for-token exchange, and a
/// provider's own token refresh) resolves through this single seam, so a channel's own client id is never
/// silently shadowed by — nor silently mixed with — the shared app's.
/// </summary>
public interface IChannelCredentialsResolver
{
    /// <summary>
    /// Resolves the OAuth app credentials to use for <paramref name="channelId"/> on <paramref name="provider"/>:
    /// the channel's own client id + secret when both are stored, else the platform's system-level credentials.
    /// Fails with <c>PROVIDER_NOT_CONFIGURED</c> (never a null or a malformed request) when neither source
    /// configures both fields.
    /// </summary>
    Task<Result<SystemAppCredentials>> ResolveAsync(
        Guid channelId,
        string provider,
        CancellationToken cancellationToken = default
    );
}
