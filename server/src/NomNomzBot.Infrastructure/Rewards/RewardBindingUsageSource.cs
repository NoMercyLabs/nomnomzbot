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
