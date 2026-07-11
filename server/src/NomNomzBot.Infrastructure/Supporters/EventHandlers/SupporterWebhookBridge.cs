// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Supporters.EventHandlers;

/// <summary>
/// Bridges the shared inbound-webhook plane (webhooks.md) into supporter ingest (supporter-events.md §0 D3):
/// when a verified webhook lands for a monetization provider, its already-journaled payload is handed to
/// <see cref="ISupporterIngestService"/>. This reuses the plane's HMAC/token verification and journal dedup;
/// supporter-specific normalization + the default-deny enabled check happen in the ingest service.
/// </summary>
public sealed class SupporterWebhookBridge : IEventHandler<InboundWebhookReceivedEvent>
{
    private readonly IEventJournal _journal;
    private readonly ISupporterIngestService _ingest;
    private readonly ILogger<SupporterWebhookBridge> _logger;

    public SupporterWebhookBridge(
        IEventJournal journal,
        ISupporterIngestService ingest,
        ILogger<SupporterWebhookBridge> logger
    )
    {
        _journal = journal;
        _ingest = ingest;
        _logger = logger;
    }

    /// <summary>Maps a webhook adapter kind to its supporter source key; null = not a monetization provider.</summary>
    private static string? SupporterSourceFor(WebhookAdapterKind adapter) =>
        adapter switch
        {
            WebhookAdapterKind.Kofi => "kofi",
            _ => null,
        };

    public async Task HandleAsync(
        InboundWebhookReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        string? sourceKey = SupporterSourceFor(@event.Adapter);
        if (sourceKey is null || @event.WasDuplicate || @event.BroadcasterId == Guid.Empty)
            return; // Not a supporter provider, a redelivery the journal already had, or no tenant.

        Result<EventRecord> record = await _journal.GetByEventIdAsync(
            @event.JournalEventId,
            cancellationToken
        );
        if (record.IsFailure)
        {
            _logger.LogWarning(
                "Supporter bridge could not load journal payload {EventId} for {Source} on {Channel}.",
                @event.JournalEventId,
                sourceKey,
                @event.BroadcasterId
            );
            return;
        }

        Result ingested = await _ingest.IngestAsync(
            @event.BroadcasterId,
            sourceKey,
            record.Value.PayloadJson,
            cancellationToken
        );
        if (ingested.IsFailure)
            _logger.LogWarning(
                "Supporter ingest failed for {Source} on {Channel}: {Error}",
                sourceKey,
                @event.BroadcasterId,
                ingested.ErrorMessage
            );
    }
}
