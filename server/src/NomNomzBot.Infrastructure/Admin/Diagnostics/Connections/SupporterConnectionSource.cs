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

/// <summary>The channel's supporter-event sources (donation sites). A source is connected while enabled and not in the <c>error</c> status. The auth secret never leaves the row.</summary>
public sealed class SupporterConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    private const string ErrorStatus = "error";

    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .SupporterConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .Select(c => new Row(
                c.Id,
                c.SourceKey,
                c.ConnectionMode,
                c.IsEnabled,
                c.Status,
                c.LastEventAt
            ))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Supporter,
                Provider = r.SourceKey,
                Id = r.Id.ToString(),
                AccountName = r.ConnectionMode,
                Connected = r.IsEnabled && r.Status != ErrorStatus,
                Status = r.Status,
                LastActivityAt = r.LastEventAt,
            }),
        ];
    }

    private sealed record Row(
        Guid Id,
        string SourceKey,
        string ConnectionMode,
        bool IsEnabled,
        string Status,
        DateTime? LastEventAt
    );
}
