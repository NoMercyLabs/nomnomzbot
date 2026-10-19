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
