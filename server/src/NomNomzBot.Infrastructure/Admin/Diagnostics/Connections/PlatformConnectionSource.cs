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

/// <summary>The channel's streaming platform links (Twitch, Kick, YouTube). A link holds no token, so it is always connected.</summary>
public sealed class PlatformConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<Row> rows = await db
            .PlatformConnections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ChannelId == broadcasterId && c.DeletedAt == null)
            .Select(c => new Row(c.Id, c.Provider, c.DisplayName, c.ExternalChannelId))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Platform,
                Provider = r.Provider,
                Id = r.Id.ToString(),
                AccountName = r.DisplayName,
                AccountId = r.ExternalChannelId,
                Connected = true,
                Status = "linked",
            }),
        ];
    }

    private sealed record Row(
        Guid Id,
        string Provider,
        string DisplayName,
        string ExternalChannelId
    );
}
