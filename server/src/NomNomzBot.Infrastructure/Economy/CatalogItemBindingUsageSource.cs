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

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>The catalog items that run a given command directly, for the command delete and disable guard.</summary>
public sealed class CatalogItemBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A catalog item stores a command by guid only; a built-in's catalogue key can never match.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        var rows = await db
            .CatalogItems.AsNoTracking()
            .Where(i =>
                i.BroadcasterId == broadcasterId
                && i.DeletedAt == null
                && i.TargetKind == BindingTargetKind.Command
                && i.TargetCommandId == commandId
            )
            .Select(i => new { i.Id, i.Name })
            .ToListAsync(ct);

        return rows.Select(i => new BindingUsage("catalog-item", i.Id.ToString(), i.Name)).ToList();
    }
}
