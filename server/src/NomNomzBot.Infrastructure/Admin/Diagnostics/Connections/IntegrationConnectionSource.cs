// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;

/// <summary>
/// The channel's vault connections (Twitch, Spotify, YouTube, Patreon, ...). A connection that may still hold a
/// working token is opened through <see cref="IIntegrationTokenVault"/>: a token it cannot decrypt reports
/// decrypt failed, and the vault marks the connection as it does on any real use. The opened value is dropped on
/// the spot and never leaves this class. The missing scopes of the Twitch connection come from
/// <see cref="IScopeNotificationService"/>, the same read the channel's own scope banner uses.
/// </summary>
public sealed class IntegrationConnectionSource(
    IApplicationDbContext db,
    IIntegrationTokenVault vault,
    IScopeNotificationService scopes
) : IChannelConnectionSource
{
    private const string DecryptFailedCode = "DECRYPT_FAILED";

    private static readonly string[] OpenableStatuses =
    [
        AuthEnums.IntegrationStatus.Connected,
        AuthEnums.IntegrationStatus.Expired,
        AuthEnums.IntegrationStatus.NeedsReauth,
    ];

    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ConnectionRow> connections = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .Select(c => new ConnectionRow(
                c.Id,
                c.Provider,
                c.ProviderAccountId,
                c.ProviderAccountName,
                c.Status,
                c.Scopes,
                c.LastErrorAt,
                c.ConsecutiveFailureCount,
                c.LastRefreshedAt ?? c.ConnectedAt
            ))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, DateTime?> expiries = await ReadAccessExpiriesAsync(
            [.. connections.Select(c => c.Id)],
            cancellationToken
        );
        HashSet<Guid> undecryptable = await FindUndecryptableAsync(connections, cancellationToken);

        List<ChannelDiagnosticConnectionDto> rows = [];
        foreach (ConnectionRow connection in connections)
        {
            bool decryptFailed =
                undecryptable.Contains(connection.Id)
                || connection.Status == AuthEnums.IntegrationStatus.DecryptFailed;
            string status = decryptFailed
                ? AuthEnums.IntegrationStatus.DecryptFailed
                : connection.Status;

            rows.Add(
                new()
                {
                    Kind = ChannelConnectionKinds.Integration,
                    Provider = connection.Provider,
                    Id = connection.Id.ToString(),
                    AccountName = connection.AccountName,
                    AccountId = connection.AccountId,
                    Connected = status == AuthEnums.IntegrationStatus.Connected,
                    Status = status,
                    NeedsReauth = status == AuthEnums.IntegrationStatus.NeedsReauth,
                    DecryptFailed = decryptFailed,
                    MissingScopes = await MissingScopesAsync(
                        broadcasterId,
                        connection.Provider,
                        cancellationToken
                    ),
                    GrantedScopes = connection.Scopes,
                    TokenExpiresAt = expiries.GetValueOrDefault(connection.Id),
                    LastErrorAt = connection.LastErrorAt,
                    ConsecutiveFailures = connection.ConsecutiveFailures,
                    LastActivityAt = connection.LastActivityAt,
                }
            );
        }

        return rows;
    }

    private async Task<Dictionary<Guid, DateTime?>> ReadAccessExpiriesAsync(
        List<Guid> connectionIds,
        CancellationToken cancellationToken
    )
    {
        List<TokenRow> tokens = await db
            .IntegrationTokens.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                connectionIds.Contains(t.ConnectionId)
                && t.TokenType == AuthEnums.TokenType.Access
                && t.DeletedAt == null
            )
            .Select(t => new TokenRow(t.ConnectionId, t.ExpiresAt))
            .ToListAsync(cancellationToken);

        return tokens
            .GroupBy(t => t.ConnectionId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.ExpiresAt));
    }

    private async Task<HashSet<Guid>> FindUndecryptableAsync(
        List<ConnectionRow> connections,
        CancellationToken cancellationToken
    )
    {
        HashSet<Guid> undecryptable = [];
        foreach (ConnectionRow connection in connections)
        {
            if (!OpenableStatuses.Contains(connection.Status))
                continue;

            Result<DecryptedTokenDto> opened = await vault.GetAccessTokenAsync(
                connection.Id,
                cancellationToken
            );
            if (opened is { IsFailure: true, ErrorCode: DecryptFailedCode })
                undecryptable.Add(connection.Id);
        }

        return undecryptable;
    }

    private async Task<IReadOnlyList<string>> MissingScopesAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken
    )
    {
        if (provider != AuthEnums.IntegrationProvider.Twitch)
            return [];

        Result<MissingScopesDto> missing = await scopes.GetMissingScopesAsync(
            broadcasterId,
            cancellationToken
        );
        return missing.IsFailure ? [] : [.. missing.Value.Scopes.Select(s => s.Scope)];
    }

    private sealed record ConnectionRow(
        Guid Id,
        string Provider,
        string? AccountId,
        string? AccountName,
        string Status,
        List<string> Scopes,
        DateTime? LastErrorAt,
        int ConsecutiveFailures,
        DateTime? LastActivityAt
    );

    private sealed record TokenRow(Guid ConnectionId, DateTime? ExpiresAt);
}
