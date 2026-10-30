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

/// <summary>The channel's OBS link. It is connected when enabled, it connected once and the last attempt left no error. The password and bridge token never leave the row.</summary>
public sealed class ObsConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .ObsConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .Select(c => new Row(c.Id, c.Host, c.Port, c.IsEnabled, c.LastConnectedAt, c.LastError))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Obs,
                Provider = "obs",
                Id = r.Id.ToString(),
                AccountName = Endpoint(r.Host, r.Port),
                Connected = r is { IsEnabled: true, LastError: null, LastConnectedAt: not null },
                Status = r.IsEnabled ? "enabled" : "disabled",
                LastError = r.LastError,
                LastActivityAt = r.LastConnectedAt,
            }),
        ];
    }

    private static string? Endpoint(string? host, int? port)
    {
        if (host is null)
            return null;
        if (port is null)
            return host;
        return $"{host}:{port}";
    }

    private sealed record Row(
        Guid Id,
        string? Host,
        int? Port,
        bool IsEnabled,
        DateTime? LastConnectedAt,
        string? LastError
    );
}
