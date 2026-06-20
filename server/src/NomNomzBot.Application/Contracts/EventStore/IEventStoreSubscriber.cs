// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
        CancellationToken cancellationToken = default
    )
        where TEvent : class, IDomainEvent;
}
