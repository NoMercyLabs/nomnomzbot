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

/// <summary>The channel's VTube Studio link. It is connected when enabled and its stored status is <c>connected</c>. The plugin token and bridge token never leave the row.</summary>
public sealed class VtsConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    private const string ConnectedStatus = "connected";

    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .VtsConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .Select(c => new Row(c.Id, c.Endpoint, c.IsEnabled, c.Status, c.LastConnectedAt))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Vts,
                Provider = "vts",
                Id = r.Id.ToString(),
                AccountName = r.Endpoint,
                Connected = r is { IsEnabled: true, Status: ConnectedStatus },
                Status = r.Status,
                LastActivityAt = r.LastConnectedAt,
            }),
        ];
    }

    private sealed record Row(
        Guid Id,
        string Endpoint,
        bool IsEnabled,
        string Status,
        DateTime? LastConnectedAt
    );
}
