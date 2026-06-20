// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>Records every published domain event so a test can assert the events the transport emitted.</summary>
public sealed class CapturingEventBus : IEventBus
{
    private readonly ConcurrentQueue<IDomainEvent> _events = new();

    public IReadOnlyList<IDomainEvent> Published => _events.ToList();

    public IEnumerable<T> EventsOf<T>()
        where T : IDomainEvent => _events.OfType<T>();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        _events.Enqueue(@event);
        return Task.CompletedTask;
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent => _events.Enqueue(@event);
}
