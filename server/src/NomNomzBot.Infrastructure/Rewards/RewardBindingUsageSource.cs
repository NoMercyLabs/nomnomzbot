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

namespace NomNomzBot.Infrastructure.Rewards;

/// <summary>The rewards that run a given command directly, for the command delete and disable guard.</summary>
public sealed class RewardBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A reward stores a command by guid only; a built-in's catalogue key can never match.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        List<(Guid Id, string Title)> rows = (
            await db
                .Rewards.AsNoTracking()
                .Where(r =>
                    r.BroadcasterId == broadcasterId
                    && r.TargetKind == BindingTargetKind.Command
                    && r.TargetCommandId == commandId
                )
                .Select(r => new { r.Id, r.Title })
                .ToListAsync(ct)
        )
            .Select(r => (r.Id, r.Title))
            .ToList();

        return rows.Select(r => new BindingUsage("reward", r.Id.ToString(), r.Title)).ToList();
    }
}
