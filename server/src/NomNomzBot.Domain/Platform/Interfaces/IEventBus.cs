// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
