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

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>The giveaways whose prize runs a given command directly, for the command delete and disable guard.</summary>
public sealed class GiveawayBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A giveaway stores a command by guid only; a built-in's catalogue key can never match.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        List<(Guid Id, string Title)> rows = (
            await db
                .Giveaways.AsNoTracking()
                .Where(g =>
                    g.BroadcasterId == broadcasterId
                    && g.TargetKind == BindingTargetKind.Command
                    && g.TargetCommandId == commandId
                )
                .Select(g => new { g.Id, g.Title })
                .ToListAsync(ct)
        )
            .Select(g => (g.Id, g.Title))
            .ToList();

        return rows.Select(g => new BindingUsage("giveaway", g.Id.ToString(), g.Title)).ToList();
    }
}
