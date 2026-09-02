// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Broadcasts a failed or dead-lettered outbound webhook delivery attempt to dashboard clients (S099). A
/// successful (<see cref="WebhookDeliveryStatus.Delivered"/>) attempt is not pushed — the operator only
/// needs a live signal for the states that require attention; the Webhooks screen's delivery list already
/// shows the full history including successes.
/// </summary>
public sealed class WebhookDeliveryAttemptedBroadcastHandler
    : IEventHandler<OutboundWebhookAttemptedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public WebhookDeliveryAttemptedBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(OutboundWebhookAttemptedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;
        if (@event.Status is not (WebhookDeliveryStatus.Failed or WebhookDeliveryStatus.DeadLetter))
            return Task.CompletedTask;

        string method =
            @event.Status == WebhookDeliveryStatus.DeadLetter
                ? "webhook_delivery_dead_letter"
                : "webhook_delivery_failed";

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            method,
            new WebhookDeliveryAttemptFailedAlertDto(
                @event.OutboundEndpointId.ToString(),
                @event.WebhookMessageId.ToString(),
                @event.Attempt,
                @event.Status.ToString(),
                @event.ResponseCode,
                @event.NextRetryAt
            ),
            ct
        );
    }
}

/// <summary>Broadcasts an outbound webhook endpoint auto-disable (too many consecutive failures) to dashboard clients.</summary>
public sealed class WebhookEndpointAutoDisabledBroadcastHandler
    : IEventHandler<OutboundWebhookAutoDisabledEvent>
{
    private readonly IDashboardNotifier _notifier;

    public WebhookEndpointAutoDisabledBroadcastHandler(IDashboardNotifier notifier) =>
        _notifier = notifier;

    public Task HandleAsync(OutboundWebhookAutoDisabledEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "webhook_endpoint_auto_disabled",
            new WebhookEndpointAutoDisabledAlertDto(
                @event.OutboundEndpointId.ToString(),
                @event.ConsecutiveFailureCount,
                @event.Reason
            ),
            ct
        );
    }
}
