// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Auth;

/// <summary>
/// Tracks revoked auth sessions (the <c>sid</c> claim minted by <see cref="IJwtTokenService"/>) so an access
/// token already in flight stops authenticating the instant its session ends (logout / impersonation-end),
/// without shortening the access-token lifetime (S098b). Revocation is recorded in the durable
/// <c>ICacheService</c> (Redis where configured, in-process memory otherwise) and fronted by a short-lived
/// per-process cache so a hot path re-checking the same session repeatedly does not re-hit the store on
/// every request.
/// </summary>
public interface ISessionRevocationService
{
    /// <summary>
    /// Marks <paramref name="sessionId"/> revoked. Any access token carrying this <c>sid</c> fails
    /// authentication on its next validated request, on this instance immediately and on any other
    /// instance within one revocation-cache window.
    /// </summary>
    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="sessionId"/> has been revoked. Consults the short-lived local cache
    /// first; only falls through to the durable store on a local cache miss.
    /// </summary>
    Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
