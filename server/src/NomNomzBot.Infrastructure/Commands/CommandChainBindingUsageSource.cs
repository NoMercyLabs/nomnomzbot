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

/// <summary>The commands that run another command directly (surface key <c>commands</c>).</summary>
public sealed class CommandChainBindingUsageSource(IApplicationDbContext db) : IBindingUsageSource
{
    public const string Surface = "commands";

    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        // A command stores its target command by guid only; a built-in's catalogue key never matches.
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        // The id is turned into text after the query: a provider may render a guid in upper case.
        var rows = await db
            .Commands.AsNoTracking()
            .Where(c =>
                c.BroadcasterId == broadcasterId
                && c.DeletedAt == null
                && c.TargetKind == BindingTargetKind.Command
                && c.TargetCommandId == commandId
            )
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        return rows.Select(c => new BindingUsage(Surface, c.Id.ToString(), c.Name)).ToList();
    }
}
