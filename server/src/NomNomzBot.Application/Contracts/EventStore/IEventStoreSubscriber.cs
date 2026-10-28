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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// The single integration point between <c>IEventBus</c> delivery and the journal: maps a bus
/// <see cref="IDomainEvent"/> to an <see cref="AppendEventRequest"/> and appends it (idempotent on the event's
/// id). Invoked from the publish path by the <c>JournalingEventBusDecorator</c>, not via per-event handlers.
/// </summary>
public interface IEventStoreSubscriber
{
    Task<Result<EventRecord>> CaptureAsync<TEvent>(
        TEvent @event,
        JournalActor? actor = null,
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IDomainEvent;
}
