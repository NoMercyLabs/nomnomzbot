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

namespace NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;

/// <summary>
/// The channel's legacy provider rows (<c>Service</c>) that older integrations still write. A row is connected
/// while enabled and holding an access token; the query asks only whether a token exists, so no token, refresh
/// token or client secret is ever read.
/// </summary>
public sealed class ServiceRowConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .Services.AsNoTracking()
            .Where(s => s.BroadcasterId == broadcasterId)
            .Select(s => new Row(
                s.Id,
                s.Name,
                s.UserName,
                s.UserId,
                s.Enabled,
                s.AccessToken != null,
                s.Scopes,
                s.TokenExpiry
            ))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Service,
                Provider = r.Name.ToLowerInvariant(),
                Id = r.Id,
                AccountName = r.UserName,
                AccountId = r.UserId,
                Connected = r is { Enabled: true, HasAccessToken: true },
                Status = r.Enabled ? "enabled" : "disabled",
                GrantedScopes = r.Scopes,
                TokenExpiresAt = r.TokenExpiry,
            }),
        ];
    }

    private sealed record Row(
        string Id,
        string Name,
        string? UserName,
        string? UserId,
        bool Enabled,
        bool HasAccessToken,
        string[] Scopes,
        DateTime? TokenExpiry
    );
}
