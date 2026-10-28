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
