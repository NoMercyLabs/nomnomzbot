// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Owns the <c>AuthSessions</c> + <c>RefreshTokens</c> lifecycle (identity-auth §3.3). Refresh tokens are
/// stored only as a SHA-256 hash, are single-use, and rotate: each rotation consumes the presented token
/// and issues a successor (linked via <c>PreviousTokenHash</c>). Presenting an already consumed/revoked
/// token is reuse — the whole session lineage is revoked and a reuse event is emitted.
/// </summary>
public sealed class SessionService : ISessionService
{
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IApplicationDbContext db,
        IJwtTokenService jwt,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SessionService> logger
    )
    {
        _db = db;
        _jwt = jwt;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<SessionTokensDto>> CreateSessionAsync(
        Guid userId,
        Guid? broadcasterId,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure<SessionTokensDto>("User not found.", "NOT_FOUND");

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        AuthSession session = new()
        {
            UserId = userId,
            BroadcasterId = broadcasterId,
            ClientType = context.ClientType,
            UserAgent = context.UserAgent,
            LastSeenAt = now,
            ExpiresAt = now.Add(SessionLifetime),
        };
        _db.AuthSessions.Add(session);

        (string rawRefresh, RefreshToken refreshToken) = NewRefreshToken(
            session.Id,
            userId,
            previousHash: null,
            now
        );
        _db.RefreshTokens.Add(refreshToken);

        await _db.SaveChangesAsync(cancellationToken);

        string accessToken = _jwt.GenerateAccessToken(
            userId,
            user.Username,
            broadcasterId,
            session.Id,
            RolesFor(user)
        );

        await _eventBus.PublishAsync(
            new UserLoggedInEvent
            {
                BroadcasterId = broadcasterId ?? Guid.Empty,
                UserId = userId,
                SessionId = session.Id,
                ClientType = context.ClientType,
            },
            cancellationToken
        );

        return Result.Success(
            new SessionTokensDto(
                accessToken,
                rawRefresh,
                now.Add(AccessLifetime),
                refreshToken.ExpiresAt,
                session.Id
            )
        );
    }

    public async Task<Result<SessionTokensDto>> RotateAsync(
        string rawRefreshToken,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        string hash = Hash(rawRefreshToken);
        RefreshToken? current = await _db.RefreshTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash,
            cancellationToken
        );
        if (current is null)
            return Result.Failure<SessionTokensDto>("Invalid refresh token.", "INVALID_TOKEN");

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        // Reuse of a consumed/revoked token: revoke the whole session lineage and fail closed.
        if (current.ConsumedAt is not null || current.RevokedAt is not null)
        {
            await RevokeSessionAsync(
                current.SessionId,
                AuthEnums.RefreshTokenRevokedReason.ReuseDetected,
                cancellationToken
            );
            await _eventBus.PublishAsync(
                new RefreshTokenReuseDetectedEvent
                {
                    UserId = current.UserId,
                    SessionId = current.SessionId,
                    TokenHash = hash,
                },
                cancellationToken
            );
            return Result.Failure<SessionTokensDto>("Refresh token reuse detected.", "TOKEN_REUSE");
        }

        if (current.ExpiresAt <= now)
            return Result.Failure<SessionTokensDto>("Refresh token expired.", "TOKEN_EXPIRED");

        AuthSession? session = await _db.AuthSessions.FirstOrDefaultAsync(
            s => s.Id == current.SessionId,
            cancellationToken
        );
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
            return Result.Failure<SessionTokensDto>(
                "Session is no longer valid.",
                "SESSION_INVALID"
            );

        User? user = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == current.UserId,
            cancellationToken
        );
        if (user is null)
            return Result.Failure<SessionTokensDto>("User not found.", "NOT_FOUND");

        current.ConsumedAt = now;
        current.RevokedReason = AuthEnums.RefreshTokenRevokedReason.Rotation;
        session.LastSeenAt = now;

        (string rawRefresh, RefreshToken successor) = NewRefreshToken(
            session.Id,
            user.Id,
            previousHash: current.TokenHash,
            now
        );
        _db.RefreshTokens.Add(successor);

        await _db.SaveChangesAsync(cancellationToken);

        string accessToken = _jwt.GenerateAccessToken(
            user.Id,
            user.Username,
            session.BroadcasterId,
            session.Id,
            RolesFor(user)
        );

        return Result.Success(
            new SessionTokensDto(
                accessToken,
                rawRefresh,
                now.Add(AccessLifetime),
                successor.ExpiresAt,
                session.Id
            )
        );
    }

    public async Task<Result> RevokeSessionAsync(
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        AuthSession? session = await _db.AuthSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId,
            cancellationToken
        );
        if (session is null)
            return Result.Success(); // idempotent

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        session.RevokedAt ??= now;

        List<RefreshToken> tokens = await _db
            .RefreshTokens.Where(t => t.SessionId == sessionId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (RefreshToken token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<int>> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        List<AuthSession> sessions = await _db
            .AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (AuthSession session in sessions)
            session.RevokedAt = now;

        List<RefreshToken> tokens = await _db
            .RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (RefreshToken token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(sessions.Count);
    }

    public async Task<Result<AuthSessionDto>> ValidateSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default
    )
    {
        AuthSession? session = await _db.AuthSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId,
            cancellationToken
        );
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
            return Result.Failure<AuthSessionDto>(
                "Session is revoked, expired, or missing.",
                "SESSION_INVALID"
            );

        session.LastSeenAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(
            new AuthSessionDto(
                session.Id,
                session.UserId,
                session.BroadcasterId,
                session.ClientType,
                session.LastSeenAt,
                session.ExpiresAt,
                session.RevokedAt is not null
            )
        );
    }

    private (string Raw, RefreshToken Token) NewRefreshToken(
        Guid sessionId,
        Guid userId,
        string? previousHash,
        DateTime now
    )
    {
        string raw = _jwt.GenerateRefreshTokenValue();
        RefreshToken token = new()
        {
            SessionId = sessionId,
            UserId = userId,
            TokenHash = Hash(raw),
            PreviousTokenHash = previousHash,
            IssuedAt = now,
            ExpiresAt = now.Add(RefreshLifetime),
        };
        return (raw, token);
    }

    private static IEnumerable<string> RolesFor(User user) =>
        user.IsPlatformPrincipal ? ["user", "admin"] : ["user"];

    private static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
