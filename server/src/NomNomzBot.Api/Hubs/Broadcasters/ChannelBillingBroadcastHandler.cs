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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A channel's own billing changes reach its open dashboards live: the plan, the invoices and the founders
/// badge reload on the Billing page. Every channel-level billing event is forwarded as a <c>billing</c> config
/// change, so the page needs no bespoke hub event.
/// </summary>
public sealed class ChannelBillingBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<SubscriptionTierChangedEvent>,
        IEventHandler<SubscriptionStatusChangedEvent>,
        IEventHandler<SubscriptionCanceledEvent>,
        IEventHandler<InvoicePaymentRecordedEvent>,
        IEventHandler<InvoiceRefundedEvent>,
        IEventHandler<InviteCodeRedeemedEvent>,
        IEventHandler<FoundersBadgeGrantedEvent>
{
    private const string Domain = "billing";

    public Task HandleAsync(SubscriptionTierChangedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, @event.SubscriptionId, "updated", ct);

    public Task HandleAsync(
        SubscriptionStatusChangedEvent @event,
        CancellationToken ct = default
    ) => PushAsync(@event, @event.SubscriptionId, "updated", ct);

    public Task HandleAsync(SubscriptionCanceledEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, @event.SubscriptionId, "updated", ct);

    public Task HandleAsync(InvoicePaymentRecordedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, null, "created", ct);

    public Task HandleAsync(InvoiceRefundedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, null, "updated", ct);

    public Task HandleAsync(InviteCodeRedeemedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, null, "updated", ct);

    public Task HandleAsync(FoundersBadgeGrantedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event, null, "created", ct);

    private Task PushAsync(
        DomainEventBase @event,
        Guid? entity,
        string action,
        CancellationToken ct
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = @event.BroadcasterId.ToString();

        return notifier.SendConfigChangedAsync(
            channel,
            new ConfigChangedDto(channel, Domain, entity?.ToString(), action),
            ct
        );
    }
}
