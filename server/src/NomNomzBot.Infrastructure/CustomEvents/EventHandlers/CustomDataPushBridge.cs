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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.CustomEvents.EventHandlers;

public sealed class CustomDataPushBridge : IEventHandler<InboundWebhookReceivedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly IEventJournal _journal;
    private readonly ICustomDataIngestService _ingest;
    private readonly ILogger<CustomDataPushBridge> _logger;

    public CustomDataPushBridge(
        IApplicationDbContext db,
        IEventJournal journal,
        ICustomDataIngestService ingest,
        ILogger<CustomDataPushBridge> logger
    )
    {
        _db = db;
        _journal = journal;
        _ingest = ingest;
        _logger = logger;
    }

    public async Task HandleAsync(
        InboundWebhookReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.WasDuplicate || @event.BroadcasterId == Guid.Empty)
            return; // A redelivery the journal already had, or no tenant.

        CustomDataSource? source = await _db.CustomDataSources.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == @event.BroadcasterId
                && s.InboundWebhookEndpointId == @event.InboundEndpointId,
            cancellationToken
        );
        if (source is null)
            return; // The endpoint belongs to something other than a custom data source.

        Result<EventRecord> record = await _journal.GetByEventIdAsync(
            @event.JournalEventId,
            cancellationToken
        );
        if (record.IsFailure)
        {
            _logger.LogWarning(
                "Custom data push bridge could not load journal payload {EventId} for {Source} on {Channel}.",
                @event.JournalEventId,
                source.Name,
                @event.BroadcasterId
            );
            return;
        }

        Result ingested = await _ingest.IngestAsync(
            source.BroadcasterId,
            source.Name,
            FlatPayloadUnflattener.Unflatten(record.Value.PayloadJson),
            cancellationToken
        );
        if (ingested.IsFailure)
            _logger.LogWarning(
                "Custom data push ingest failed for {Source} on {Channel}: {Error}",
                source.Name,
                @event.BroadcasterId,
                ingested.ErrorMessage
            );
    }
}
