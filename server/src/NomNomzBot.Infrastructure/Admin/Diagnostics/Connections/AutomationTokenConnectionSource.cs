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

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;

/// <summary>
/// The channel's automation API tokens. A token is connected while it is neither revoked nor expired. Only its
/// name, scopes and times leave the row, never the hash and not even the visible prefix.
/// </summary>
public sealed class AutomationTokenConnectionSource(IApplicationDbContext db, TimeProvider clock)
    : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .AutomationApiTokens.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.BroadcasterId == broadcasterId && t.DeletedAt == null)
            .Select(t => new Row(
                t.Id,
                t.Name,
                t.ScopesJson,
                t.LastUsedAt,
                t.ExpiresAt,
                t.RevokedAt
            ))
            .ToListAsync(cancellationToken);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        return
        [
            .. rows.Select(r =>
            {
                string status = StatusOf(r, now);
                return new ChannelDiagnosticConnectionDto
                {
                    Kind = ChannelConnectionKinds.AutomationToken,
                    Provider = "automation",
                    Id = r.Id.ToString(),
                    AccountName = r.Name,
                    Connected = status == "active",
                    Status = status,
                    GrantedScopes = ParseScopes(r.ScopesJson),
                    TokenExpiresAt = r.ExpiresAt,
                    LastActivityAt = r.LastUsedAt,
                };
            }),
        ];
    }

    private static string StatusOf(Row row, DateTime now)
    {
        if (row.RevokedAt is not null)
            return "revoked";
        if (row.ExpiresAt is { } expiresAt && expiresAt <= now)
            return "expired";
        return "active";
    }

    private static IReadOnlyList<string> ParseScopes(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record Row(
        Guid Id,
        string Name,
        string ScopesJson,
        DateTime? LastUsedAt,
        DateTime? ExpiresAt,
        DateTime? RevokedAt
    );
}
