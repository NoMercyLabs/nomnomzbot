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
/// Owns the <c>AuthSessions</c> + <c>RefreshTokens</c> lifecycle (identity-auth §3.3), extracted from
/// <c>IAuthService</c> for single responsibility. Refresh tokens are hashed, single-use, and rotate: each
/// rotation consumes the presented token and issues a successor, and presenting an already
/// consumed/revoked token is treated as reuse — the whole session lineage is revoked.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Inserts an <c>AuthSession</c> + an initial hashed <c>RefreshToken</c> and returns the access JWT plus
    /// the RAW refresh token (returned once; only the hash is persisted).
    /// </summary>
    Task<Result<SessionTokensDto>> CreateSessionAsync(
        Guid userId,
        Guid? broadcasterId,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Rotates by raw refresh token: if already consumed/revoked → reuse (revoke the lineage, emit
    /// <c>RefreshTokenReuseDetectedEvent</c>, fail). Else consumes it, issues a successor, and returns fresh
    /// tokens.
    /// </summary>
    Task<Result<SessionTokensDto>> RotateAsync(
        string rawRefreshToken,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );

    /// <summary>Revokes a session and all its non-revoked refresh tokens with the given reason.</summary>
    Task<Result> RevokeSessionAsync(
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Revokes every active session + refresh token for a user (logout-all / erasure). Returns the count
    /// revoked.
    /// </summary>
    Task<Result<int>> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the session if it is not revoked/expired and bumps <c>LastSeenAt</c>; fails when
    /// revoked/expired/missing.
    /// </summary>
    Task<Result<AuthSessionDto>> ValidateSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default
    );
}
