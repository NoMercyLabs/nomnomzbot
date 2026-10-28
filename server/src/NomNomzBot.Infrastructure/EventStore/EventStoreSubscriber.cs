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

using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// The single integration point between bus delivery and the journal. Maps a bus <see cref="IDomainEvent"/> to
/// an <see cref="AppendEventRequest"/> and appends it (idempotent on the event's id). The event type discriminator
/// is the CLR type name; the payload is the Newtonsoft.Json serialization of the event; the version is the
/// current registered upcaster version for that type. <c>BroadcasterId.Empty</c> (the platform-level sentinel)
/// maps to a <c>null</c> tenant so the journal treats it as the platform-global stream.
/// </summary>
public sealed class EventStoreSubscriber : IEventStoreSubscriber
{
    private readonly IEventJournal _journal;
    private readonly IEventUpcasterRegistry _upcasters;

    public EventStoreSubscriber(IEventJournal journal, IEventUpcasterRegistry upcasters)
    {
        _journal = journal;
        _upcasters = upcasters;
    }

    public Task<Result<EventRecord>> CaptureAsync<TEvent>(
        TEvent @event,
        JournalActor? actor = null,
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IDomainEvent
    {
        string eventType = typeof(TEvent).Name;

        // DomainEventBase stamps EventId as a UUIDv7 Guid — the journal keys on it directly (no parse).
        Guid? broadcasterId = @event.BroadcasterId == Guid.Empty ? null : @event.BroadcasterId;

        AppendEventRequest request = new(
            EventId: @event.EventId,
            BroadcasterId: broadcasterId,
            EventType: eventType,
            EventVersion: _upcasters.CurrentVersion(eventType),
            Source: "domain",
            PayloadJson: JsonConvert.SerializeObject(@event),
            MetadataJson: "{}",
            OccurredAt: @event.OccurredAt.UtcDateTime
        );

        if (@event is IActorAttributedEvent && actor is not null)
            request = request with
            {
                ActorUserId = actor.UserId,
                MetadataJson = JournalActorMetadata.ToJson(actor),
            };

        return _journal.AppendAsync(request, cancellationToken);
    }
}
