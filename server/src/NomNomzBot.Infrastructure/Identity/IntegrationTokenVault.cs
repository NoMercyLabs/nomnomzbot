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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The crypto-shred-ready OAuth token vault (identity-auth §3.4). Owns <c>IntegrationConnection</c> +
/// <c>IntegrationToken</c>. Secrets are sealed by the canonical <see cref="ITokenProtector"/>
/// (per-subject DEK + AES-256-GCM AEAD over <see cref="ISubjectKeyService"/>) — this vault hand-rolls no
/// crypto; it only stores the sealed envelope and references the DEK that opens it. The plaintext token is
/// never persisted; reading it fails closed once the subject DEK is crypto-shredded.
/// </summary>
public sealed class IntegrationTokenVault : IIntegrationTokenVault
{
    private const int NeedsReauthThreshold = 3;
    private const string PlatformSubject = "_platform";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ISubjectKeyService _subjectKeys;
    private readonly IScopeGrantService _scopeGrant;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrationTokenVault> _logger;

    public IntegrationTokenVault(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ISubjectKeyService subjectKeys,
        IScopeGrantService scopeGrant,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<IntegrationTokenVault> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _subjectKeys = subjectKeys;
        _scopeGrant = scopeGrant;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
        UpsertConnectionDto request,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == request.BroadcasterId
                    && c.Provider == request.Provider
                    && c.ProviderAccountId == request.ProviderAccountId,
                cancellationToken
            );

        bool isFirstConnect = connection is null;
        if (connection is null)
        {
            connection = new()
            {
                BroadcasterId = request.BroadcasterId,
                Provider = request.Provider,
                ProviderAccountId = request.ProviderAccountId,
                ProviderAccountName = request.ProviderAccountName,
                Status = AuthEnums.IntegrationStatus.Pending,
                Scopes = [.. request.Scopes],
                ClientId = request.ClientId,
                IsByok = request.IsByok,
                Settings = request.SettingsJson,
                ConnectedByUserId = request.ConnectedByUserId,
            };
            _db.IntegrationConnections.Add(connection);
        }
        else
        {
            connection.ProviderAccountName = request.ProviderAccountName;
            connection.Scopes = [.. request.Scopes];
            connection.ClientId = request.ClientId;
            connection.IsByok = request.IsByok;
            connection.Settings = request.SettingsJson;
            connection.ConnectedByUserId = request.ConnectedByUserId;
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (isFirstConnect)
            await _eventBus.PublishAsync(
                new IntegrationConnectedEvent
                {
                    BroadcasterId = connection.BroadcasterId ?? Guid.Empty,
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    ProviderAccountId = connection.ProviderAccountId ?? string.Empty,
                },
                cancellationToken
            );

        return Result.Success(ToDto(connection));
    }

    public async Task<Result> StoreTokensAsync(
        Guid connectionId,
        StoreTokensDto tokens,
        IReadOnlyList<string>? grantedScopes = null,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
            return Result.Failure("No such connection.", "NOT_FOUND");

        string subject = SubjectId(connection.BroadcasterId);
        Guid encryptionKeyId = await ResolveKeyIdAsync(
            connection.Provider,
            subject,
            cancellationToken
        );

        await UpsertTokenAsync(
            connection,
            AuthEnums.TokenType.Access,
            tokens.AccessToken,
            tokens.AccessExpiresAt,
            encryptionKeyId,
            cancellationToken
        );
        if (tokens.RefreshToken is not null)
            await UpsertTokenAsync(
                connection,
                AuthEnums.TokenType.Refresh,
                tokens.RefreshToken,
                null,
                encryptionKeyId,
                cancellationToken
            );
        if (tokens.AppToken is not null)
            await UpsertTokenAsync(
                connection,
                AuthEnums.TokenType.App,
                tokens.AppToken,
                null,
                encryptionKeyId,
                cancellationToken
            );

        connection.Status = AuthEnums.IntegrationStatus.Connected;
        connection.ConsecutiveFailureCount = 0;
        connection.LastErrorAt = null;
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        connection.ConnectedAt ??= now;
        connection.LastRefreshedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        // Keep the stored grant set truthful (identity-auth §3.4a) on every store/refresh.
        if (grantedScopes is not null)
            await _scopeGrant.ReconcileGrantedScopesAsync(
                connectionId,
                grantedScopes,
                cancellationToken
            );

        await _eventBus.PublishAsync(
            new IntegrationTokenRefreshedEvent
            {
                BroadcasterId = connection.BroadcasterId ?? Guid.Empty,
                ConnectionId = connection.Id,
                Provider = connection.Provider,
                ExpiresAt = tokens.AccessExpiresAt ?? now,
            },
            cancellationToken
        );

        return Result.Success();
    }

    public async Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    ) => await GetTokenAsync(connectionId, AuthEnums.TokenType.Access, cancellationToken);

    public async Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    ) => await GetTokenAsync(connectionId, AuthEnums.TokenType.Refresh, cancellationToken);

    public async Task<Result> MarkRefreshFailureAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
            return Result.Failure("No such connection.", "NOT_FOUND");

        connection.ConsecutiveFailureCount++;
        connection.LastErrorAt = _timeProvider.GetUtcNow().UtcDateTime;

        bool crossedThreshold = connection.ConsecutiveFailureCount >= NeedsReauthThreshold;
        if (crossedThreshold)
            connection.Status = AuthEnums.IntegrationStatus.NeedsReauth;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Integration {ConnectionId} refresh failure #{Count}: {Error}",
            connectionId,
            connection.ConsecutiveFailureCount,
            error
        );

        if (crossedThreshold)
            await _eventBus.PublishAsync(
                new IntegrationNeedsReauthEvent
                {
                    BroadcasterId = connection.BroadcasterId ?? Guid.Empty,
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    ConsecutiveFailureCount = connection.ConsecutiveFailureCount,
                },
                cancellationToken
            );

        return Result.Success();
    }

    public async Task<Result> RevokeConnectionAsync(
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
            return Result.Success(); // idempotent

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        List<IntegrationToken> tokens = await _db
            .IntegrationTokens.IgnoreQueryFilters()
            .Where(t => t.ConnectionId == connectionId && t.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (IntegrationToken token in tokens)
            token.DeletedAt = now;

        connection.Status = AuthEnums.IntegrationStatus.Revoked;
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new IntegrationDisconnectedEvent
            {
                BroadcasterId = connection.BroadcasterId ?? Guid.Empty,
                ConnectionId = connection.Id,
                Provider = connection.Provider,
                Reason = reason,
            },
            cancellationToken
        );

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<IntegrationConnection> connections = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        IReadOnlyList<IntegrationConnectionDto> dtos = [.. connections.Select(ToDto)];
        return Result.Success(dtos);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Result<DecryptedTokenDto>> GetTokenAsync(
        Guid connectionId,
        string tokenType,
        CancellationToken cancellationToken
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
            return Result.Failure<DecryptedTokenDto>("No such connection.", "NOT_FOUND");

        IntegrationToken? token = await _db
            .IntegrationTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t =>
                    t.ConnectionId == connectionId
                    && t.TokenType == tokenType
                    && t.DeletedAt == null,
                cancellationToken
            );
        if (token is null)
            return Result.Failure<DecryptedTokenDto>("No such token.", "NOT_FOUND");

        string? plaintext = await _tokenProtector.TryUnprotectAsync(
            token.CipherText,
            new TokenProtectionContext(
                SubjectId(connection.BroadcasterId),
                connection.Provider,
                tokenType
            ),
            cancellationToken
        );
        if (plaintext is null)
            // Null means the DEK was crypto-shredded, the AAD/tag failed, or the value is malformed —
            // fail closed (the GDPR guarantee surfaced to callers).
            return Result.Failure<DecryptedTokenDto>(
                "The token could not be decrypted.",
                "DECRYPT_FAILED"
            );

        bool isExpired = token.ExpiresAt is { } exp && exp <= _timeProvider.GetUtcNow().UtcDateTime;
        return Result.Success(
            new DecryptedTokenDto(plaintext, tokenType, token.ExpiresAt, isExpired)
        );
    }

    private async Task UpsertTokenAsync(
        IntegrationConnection connection,
        string tokenType,
        string plaintext,
        DateTime? expiresAt,
        Guid encryptionKeyId,
        CancellationToken cancellationToken
    )
    {
        string sealedEnvelope = await _tokenProtector.ProtectAsync(
            plaintext,
            new TokenProtectionContext(
                SubjectId(connection.BroadcasterId),
                connection.Provider,
                tokenType
            ),
            cancellationToken
        );

        IntegrationToken? token = await _db
            .IntegrationTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.ConnectionId == connection.Id && t.TokenType == tokenType,
                cancellationToken
            );

        if (token is null)
        {
            token = new()
            {
                ConnectionId = connection.Id,
                BroadcasterId = connection.BroadcasterId,
                TokenType = tokenType,
                EncryptionKeyId = encryptionKeyId,
            };
            _db.IntegrationTokens.Add(token);
        }

        token.CipherText = sealedEnvelope;
        token.BroadcasterId = connection.BroadcasterId;
        token.EncryptionKeyId = encryptionKeyId;
        token.ExpiresAt = expiresAt;
        token.RotatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        token.DeletedAt = null;
    }

    /// <summary>
    /// The crypto subject for a connection's tokens: the tenant Guid as a string, or the platform sentinel
    /// for global connections (the shared bot). Matches <see cref="ITokenProtector"/>'s AAD subject and
    /// <c>TwitchAuthService.SubjectId</c> so the same envelope re-opens across services.
    /// </summary>
    private static string SubjectId(Guid? broadcasterId) =>
        broadcasterId?.ToString() ?? PlatformSubject;

    /// <summary>
    /// Resolves the DEK id that <see cref="ITokenProtector"/> will use for this subject+provider, so it can
    /// be recorded on <c>IntegrationToken.EncryptionKeyId</c>. Reproduces the protector's deterministic
    /// subject identity (SHA-256 of <c>provider:subject</c>) so both resolve the same key.
    /// </summary>
    private async Task<Guid> ResolveKeyIdAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken
    )
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{subject}"));
        Guid subjectUserId = new(hash.AsSpan(0, 16));
        string subjectIdHash = Convert.ToHexStringLower(hash);

        Result<Guid> keyId = await _subjectKeys.GetOrCreateSubjectKeyAsync(
            subjectUserId,
            subjectIdHash,
            cancellationToken
        );
        return keyId.IsSuccess ? keyId.Value : Guid.Empty;
    }

    private static IntegrationConnectionDto ToDto(IntegrationConnection c) =>
        new(
            c.Id,
            c.BroadcasterId,
            c.Provider,
            c.ProviderAccountId,
            c.ProviderAccountName,
            c.Status,
            [.. c.Scopes],
            c.IsByok,
            c.ConnectedAt,
            c.LastRefreshedAt,
            c.ConsecutiveFailureCount
        );
}
