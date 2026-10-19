// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
