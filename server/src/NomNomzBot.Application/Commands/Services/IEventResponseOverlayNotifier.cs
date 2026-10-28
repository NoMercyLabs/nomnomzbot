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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Abstraction that lets <c>EventResponseExecutor</c> push an <c>overlay</c>-type <see cref="Domain.Commands.Entities.EventResponse"/>
/// to the broadcaster's overlay clients without taking a direct dependency on the API layer's SignalR hub
/// context. Implemented by <c>EventResponseOverlayNotifierAdapter</c> in the API layer.
/// </summary>
public interface IEventResponseOverlayNotifier
{
    /// <summary>
    /// Broadcasts one event-response overlay trigger to every connected overlay client for the broadcaster.
    /// </summary>
    /// <param name="broadcasterId">The tenant whose overlay clients should receive the trigger.</param>
    /// <param name="eventTypeKey">The channel event that fired the response (e.g. "channel.follow").</param>
    /// <param name="resolvedMessage">The response's message template, already resolved against the trigger's variables (may be empty when the operator left it blank).</param>
    /// <param name="metadata">The response's operator-configured metadata (e.g. the target overlay widget id).</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string resolvedMessage,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default
    );
}
