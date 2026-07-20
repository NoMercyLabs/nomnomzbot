// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;

namespace NomNomzBot.Application.Abstractions.Auth;

/// <summary>
/// Mints and validates the platform access JWT (identity-auth §3.2). Signing-algorithm-agnostic: the impl
/// chooses HS256 (single-user self-host, the default) or RS256/ES256 + JWKS (federation/SSO path) behind
/// this unchanged surface. The <c>sub</c> claim is the internal user <see cref="Guid"/>; the resolved tenant
/// and session ride along as the <c>tenant</c> / <c>sid</c> claims.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Mints a short-lived access JWT: <c>sub=userId</c>, <c>tenant=broadcasterId</c>, <c>sid=sessionId</c>,
    /// plus any roles and the login-provider <c>idp</c> claim (platform-identity §3.3). Pure — no persistence.
    /// When <paramref name="actorUserId"/> is supplied the token is an act-as (impersonation) token: the
    /// non-authoritative <c>act</c>/<c>act_name</c> claims name the operator ACTING AS the subject, while
    /// <c>sub</c> and the roles remain the impersonated user's — no authorization path reads <c>act</c>.
    /// </summary>
    string GenerateAccessToken(
        Guid userId,
        string username,
        Guid? broadcasterId,
        Guid sessionId,
        IEnumerable<string>? roles = null,
        string? idp = null,
        string? actorUserId = null,
        string? actorUsername = null
    );

    /// <summary>
    /// Returns a cryptographically-random opaque refresh-token string. The CALLER hashes and persists it;
    /// the JWT layer never stores it (refresh tokens are no longer self-describing JWTs).
    /// </summary>
    string GenerateRefreshTokenValue();

    /// <summary>
    /// Validates signature + lifetime + issuer/audience and returns the principal, or null on any failure.
    /// No state change.
    /// </summary>
    ClaimsPrincipal? ValidateAccessToken(string token);
}
