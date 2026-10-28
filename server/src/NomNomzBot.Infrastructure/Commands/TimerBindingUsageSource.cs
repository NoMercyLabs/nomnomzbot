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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>The timers that run a command directly (surface key <c>timers</c>).</summary>
public sealed class TimerBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public const string Surface = "timers";

    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A timer stores a command by guid only; a built-in's catalogue key never matches.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        // The id is turned into text after the query: a provider may render a guid in upper case.
        var rows = await db
            .Timers.AsNoTracking()
            .Where(t =>
                t.BroadcasterId == broadcasterId
                && t.DeletedAt == null
                && t.TargetKind == BindingTargetKind.Command
                && t.TargetCommandId == commandId
            )
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        return rows.Select(t => new BindingUsage(Surface, t.Id.ToString(), t.Name)).ToList();
    }
}
