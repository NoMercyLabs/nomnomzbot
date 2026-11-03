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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies the event names a <c>wait_for_event</c> step can resume on, for the <c>event_name</c> picker kind. The
/// engine resumes a parked run from <c>EventResponseExecutor</c>, which passes the event type key of every event it
/// handles. So the names are the keys of the event-response catalogue (<see cref="EventResponsePresetCatalog"/>),
/// plus any other event type the asking channel has a response row for (a custom trigger). The saved value is the
/// event name; the secondary text says whether it is a catalogue event or one the channel added. Another channel's
/// rows never appear.
/// </summary>
internal sealed class EventNameOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public EventNameOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.EventName;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<string> channelTypes = await _db
            .EventResponses.Where(r => r.BroadcasterId == broadcasterId && r.DeletedAt == null)
            .Select(r => r.EventType)
            .Distinct()
            .ToListAsync(ct);

        List<PipelineOption> catalogue =
        [
            .. EventResponsePresetCatalog.EventTypes.Select(t =>
                PipelineOptionPage.Option(t, t, "Event")
            ),
        ];
        List<PipelineOption> channelOnly =
        [
            .. channelTypes
                .Where(t => !EventResponsePresetCatalog.EventTypes.Contains(t))
                .Select(t => PipelineOptionPage.Option(t, t, "Added on this channel")),
        ];

        return PipelineOptionPage.Of([.. catalogue, .. channelOnly], request);
    }
}
