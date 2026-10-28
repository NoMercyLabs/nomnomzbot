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

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Base for the per-subscription-type EventSub translators (twitch-eventsub §3.7). Supplies the injected clock
/// (concrete translators stamp <c>OccurredAt = Clock.GetUtcNow()</c> for determinism) and a small
/// <see cref="PublishAsync{TEvent}"/> helper so a concrete translator is just "read the raw payload, build the
/// typed event, publish". Publishing through this helper keeps the concrete event type at the call site, which
/// is what lets the bus resolve <c>IEventHandler&lt;TConcrete&gt;</c> without reflection.
/// </summary>
public abstract class EventSubEventTranslator(IEventBus bus, TimeProvider clock)
    : IEventSubEventTranslator
{
    protected TimeProvider Clock { get; } = clock;

    public abstract string SubscriptionType { get; }

    public abstract Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    );

    /// <summary>
    /// Publishes a domain event. <typeparamref name="TEvent"/> is inferred from the argument's concrete type, so
    /// the bus binds the correct <c>IEventHandler&lt;TEvent&gt;</c> set (the concrete translator sets the event's
    /// <c>BroadcasterId</c> and <c>Timestamp</c> when it builds it).
    /// </summary>
    protected Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : DomainEventBase
    {
        // Every EventSub-derived event is platform ingress: the bus gates it on the channel's onboarding.
        @event.IsChannelIngress = true;
        return bus.PublishAsync(@event, ct);
    }
}
