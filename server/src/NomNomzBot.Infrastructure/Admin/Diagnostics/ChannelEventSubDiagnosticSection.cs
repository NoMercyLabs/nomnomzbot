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

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>The <c>eventsub</c> section: the channel's own live EventSub subscription rows, ordered by event
/// type. Platform rows (no channel) and deleted rows are left out.</summary>
public sealed class ChannelEventSubDiagnosticSection(IApplicationDbContext db)
    : IChannelDiagnosticSection
{
    public string Key => "eventsub";

    public async Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    )
    {
        diagnostics.EventSub = await db
            .EventSubSubscriptions.AsNoTracking()
            .Where(s => s.BroadcasterId == request.BroadcasterId && s.DeletedAt == null)
            .OrderBy(s => s.EventType)
            .ThenBy(s => s.Version)
            .Select(s => new ChannelDiagnosticEventSubDto(
                s.Id,
                s.EventType,
                s.Version,
                s.Transport,
                s.Status,
                s.Enabled,
                s.Cost,
                s.LastError,
                s.ExpiresAt,
                s.UpdatedAt,
                s.ConduitId,
                s.ShardId
            ))
            .ToListAsync(cancellationToken);

        return Result.Success();
    }
}
