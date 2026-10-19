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

/// <summary>The chat triggers that run a given command directly, for the command delete and disable guard.</summary>
public sealed class ChatTriggerBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A chat trigger stores a command by guid only; a built-in's catalogue key can never match.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        List<(Guid Id, string Pattern)> rows = (
            await db
                .ChatTriggers.AsNoTracking()
                .Where(t =>
                    t.BroadcasterId == broadcasterId
                    && t.TargetKind == BindingTargetKind.Command
                    && t.TargetCommandId == commandId
                )
                .Select(t => new { t.Id, t.Pattern })
                .ToListAsync(ct)
        )
            .Select(t => (t.Id, t.Pattern))
            .ToList();

        return rows.Select(t => new BindingUsage("chat-trigger", t.Id.ToString(), t.Pattern))
            .ToList();
    }
}
