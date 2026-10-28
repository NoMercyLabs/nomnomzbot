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

namespace NomNomzBot.Domain.Platform.Interfaces;

/// <summary>
/// The single interface for publishing domain events. Registered as a singleton in DI.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered handlers.
    /// Handlers execute asynchronously. One handler's failure does not
    /// affect other handlers.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent;

    /// <summary>
    /// Publishes an event without awaiting handler completion.
    /// All handlers execute in the background. Failures are logged but not propagated.
    /// </summary>
    void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent;
}
