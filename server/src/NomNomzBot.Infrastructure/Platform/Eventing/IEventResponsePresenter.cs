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

using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The presentation half of an alert handler, callable on its own: turn one past domain event into the same
/// configured response viewers saw live (chat line, TTS, overlay response) — and nothing else. A dashboard
/// replay goes through this, so it never logs a new activity row and never repeats the handler's other work.
/// </summary>
public interface IEventResponsePresenter
{
    /// <summary>The domain event type this presenter answers for.</summary>
    Type EventType { get; }

    /// <summary>Replays the configured response for <paramref name="event"/>, which must be an
    /// <see cref="EventType"/>. An event the live handler would have stayed silent for stays silent here.</summary>
    Task<EventResponseOutcome> ReplayAsync(IDomainEvent @event, CancellationToken ct);
}
