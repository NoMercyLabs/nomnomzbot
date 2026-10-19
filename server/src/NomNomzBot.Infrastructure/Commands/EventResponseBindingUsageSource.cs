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

/// <summary>
/// The event responses that run a command directly. An event response stores a command by id only, so a
/// built-in command (catalogue key, not a guid) is never bound here.
/// </summary>
public sealed class EventResponseBindingUsageSource : IBindingUsageSource
{
    public const string Surface = "event-response";

    private readonly IApplicationDbContext _db;

    public EventResponseBindingUsageSource(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(commandTargetId, out Guid commandId))
            return [];

        List<EventResponse> rows = await _db
            .EventResponses.AsNoTracking()
            .Where(r =>
                r.BroadcasterId == broadcasterId
                && r.TargetKind == BindingTargetKind.Command
                && r.TargetCommandId == commandId
            )
            .OrderBy(r => r.EventType)
            .ToListAsync(ct);
        return [.. rows.Select(r => new BindingUsage(Surface, r.Id.ToString(), r.EventType))];
    }
}
