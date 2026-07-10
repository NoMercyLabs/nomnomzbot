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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.EventHandlers;

/// <summary>
/// Folds a successful execution into the command row's live counters (<see cref="Command.UseCount"/>,
/// <see cref="Command.LastUsedAt"/>) — the numbers the Commands page and the Home top-commands panel read.
/// The event carries the canonical (alias-resolved) name; a builtin without a Commands row is a no-op.
/// </summary>
public sealed class CommandUseCountHandler : IEventHandler<CommandExecutedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CommandUseCountHandler(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(CommandExecutedEvent @event, CancellationToken ct = default)
    {
        if (!@event.Succeeded || @event.BroadcasterId == Guid.Empty)
            return;

        string normalized = @event.CommandName.ToLowerInvariant();
        Command? command = await _db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == @event.BroadcasterId && c.NameNormalized == normalized,
            ct
        );
        if (command is null)
            return;

        command.UseCount++;
        command.LastUsedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
    }
}
