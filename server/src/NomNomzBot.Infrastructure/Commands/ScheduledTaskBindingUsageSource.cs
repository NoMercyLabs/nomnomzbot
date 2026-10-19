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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>The pending scheduled tasks that run a given command directly, for the command delete and disable guard.</summary>
public sealed class ScheduledTaskBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A scheduled task stores a command by guid only; a built-in's catalogue key can never match.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        // A fired, cancelled or expired task will never run again, so only pending ones block a delete.
        List<ScheduledPipelineTask> rows = await db
            .ScheduledPipelineTasks.AsNoTracking()
            .Where(t =>
                t.BroadcasterId == broadcasterId
                && t.Status == ScheduledPipelineTaskStatus.Pending
                && t.TargetKind == BindingTargetKind.Command
                && t.TargetCommandId == commandId
            )
            .ToListAsync(ct);

        return rows.Select(t => new BindingUsage(
                "scheduled-task",
                t.Id.ToString(),
                $"Scheduled task due {t.DueAt:u}"
            ))
            .ToList();
    }
}
