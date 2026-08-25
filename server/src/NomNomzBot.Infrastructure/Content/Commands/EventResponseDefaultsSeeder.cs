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
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Content.Commands;

/// <summary>
/// Seeds one disabled <see cref="EventResponse"/> row per <see cref="EventResponsePresetCatalog"/> event
/// type for every channel that doesn't already have one — the "top-up seed" formerly performed as a side
/// effect of <c>EventResponseService.ListAsync</c> (a GET that wrote), moved here to a real lifecycle
/// point, mirroring <see cref="DefaultCommandsSeeder"/> (Content.Commands): a full-startup <see cref="ISeeder"/>
/// pass over every channel, plus a scoped call for a single newly-onboarded channel so it doesn't wait for
/// the next boot.
/// </summary>
/// <remarks>
/// Idempotent: upserts by the natural key <c>(BroadcasterId, EventType)</c> — including SOFT-DELETED rows
/// (queried with EF Core's <c>IgnoreQueryFilters()</c>), so a channel operator who deliberately deleted a
/// default response never gets it silently re-added by this seeder. That is the
/// fix for the companion bug: deletion used to never stick because the old top-up query only looked at
/// the tenant-filtered (non-deleted) set and re-inserted anything missing from it — including rows that
/// were missing BECAUSE they'd just been deleted. A deliberate restore is a separate, explicit act — see
/// <c>EventResponseService.UpsertAsync</c>, which revives a soft-deleted row for the requested event type
/// instead of leaving it orphaned.
/// Order 81 — right after <see cref="DefaultCommandsSeeder"/> (80), same reasoning: it FK-references
/// Channel rows created at runtime by onboarding.
/// </remarks>
public sealed class EventResponseDefaultsSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public EventResponseDefaultsSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 81;

    /// <summary>The startup <see cref="ISeeder"/> pass: seeds every channel.</summary>
    public Task SeedAsync(CancellationToken ct = default) => SeedAsync(broadcasterId: null, ct);

    /// <summary>
    /// Seeds the default event responses for a single channel (<paramref name="broadcasterId"/>) or, when
    /// null, every channel. Same idempotent upsert-by-natural-key either way.
    /// </summary>
    public async Task SeedAsync(Guid? broadcasterId, CancellationToken ct = default)
    {
        List<Guid> channelIds = broadcasterId is { } id
            ? [id]
            : await _db.Channels.Select(c => c.Id).ToListAsync(ct);

        if (channelIds.Count == 0)
            return;

        // IgnoreQueryFilters: a channel with a SOFT-DELETED row for a type must not get that type
        // re-added — that would silently undo a deliberate deletion.
        List<(Guid BroadcasterId, string EventType)> existing = await _db
            .EventResponses.IgnoreQueryFilters()
            .Where(e => channelIds.Contains(e.BroadcasterId))
            .Select(e => new ValueTuple<Guid, string>(e.BroadcasterId, e.EventType))
            .ToListAsync(ct);

        HashSet<(Guid, string)> present = [.. existing];

        foreach (Guid channelId in channelIds)
        {
            foreach (string eventType in EventResponsePresetCatalog.EventTypes)
            {
                if (present.Contains((channelId, eventType)))
                    continue;

                _db.EventResponses.Add(
                    new()
                    {
                        BroadcasterId = channelId,
                        EventType = eventType,
                        IsEnabled = false,
                        ResponseType = "chat_message",
                    }
                );
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
