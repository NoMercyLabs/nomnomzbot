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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// One auth-code + PKCE login provider's OAuth mechanics (platform-identity §3.2 / §10.3), keyed by
/// <see cref="Key"/> (an <c>AuthEnums.LoginProvider</c> value). The generic <c>auth/{provider}/authorize</c>
/// route builds the authorize URL with the PKCE challenge; the <c>auth/{provider}/callback</c> route exchanges
/// the code with the stored verifier into an <see cref="ExternalIdentityProof"/> that
/// <see cref="IExternalLoginService"/> turns into a session. Kick and Twitter/X use this; device-grant
/// providers (twitch, youtube) use <see cref="ILoginIdentityProvider"/>.
/// </summary>
public interface IAuthCodeLoginProvider
{
    /// <summary>The provider key this implementation serves (e.g. <c>kick</c>, <c>twitter</c>).</summary>
    string Key { get; }

    /// <summary>Build the provider's authorize URL (response_type=code, PKCE S256 challenge, state, scopes).</summary>
    Task<Result<Uri>> BuildAuthorizeUrlAsync(
        string state,
        string redirectUri,
        string codeChallenge,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Exchange the returned authorization code (proving possession via <paramref name="codeVerifier"/>) for
    /// tokens, vault them as a user-level login connection, and surface the proven identity.
    /// </summary>
    Task<Result<ExternalIdentityProof>> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default
    );
}
