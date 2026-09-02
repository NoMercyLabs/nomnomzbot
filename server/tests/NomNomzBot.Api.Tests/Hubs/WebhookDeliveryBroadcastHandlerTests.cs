// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves outbound webhook delivery-attempt and auto-disable events reach a real consumer (S099): before
/// this slice, <see cref="OutboundWebhookAttemptedEvent"/> and <see cref="OutboundWebhookAutoDisabledEvent"/>
/// were published on every delivery attempt but had no handler at all — an endpoint could silently fail or
/// dead-letter with zero signal outside the Webhooks screen's own delivery list.
/// </summary>
public sealed class WebhookDeliveryBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_FailedAttempt_NotifiesWebhookDeliveryFailed()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookDeliveryAttemptedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid endpointId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        DateTime nextRetryAt = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                OutboundEndpointId = endpointId,
                WebhookMessageId = messageId,
                Attempt = 2,
                Status = WebhookDeliveryStatus.Failed,
                ResponseCode = 503,
                NextRetryAt = nextRetryAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "webhook_delivery_failed",
                Arg.Is<object>(data =>
                    data is WebhookDeliveryAttemptFailedAlertDto
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).OutboundEndpointId
                        == endpointId.ToString()
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).WebhookMessageId
                        == messageId.ToString()
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).Attempt == 2
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).Status == "Failed"
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).ResponseCode == 503
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).NextRetryAt == nextRetryAt
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_DeadLetterAttempt_NotifiesWebhookDeliveryDeadLetter()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookDeliveryAttemptedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                OutboundEndpointId = Guid.CreateVersion7(),
                WebhookMessageId = Guid.CreateVersion7(),
                Attempt = 20,
                Status = WebhookDeliveryStatus.DeadLetter,
                ResponseCode = null,
                NextRetryAt = null,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "webhook_delivery_dead_letter",
                Arg.Is<object>(data =>
                    data is WebhookDeliveryAttemptFailedAlertDto
                    && ((WebhookDeliveryAttemptFailedAlertDto)data).Status == "DeadLetter"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_DeliveredAttempt_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookDeliveryAttemptedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.CreateVersion7(),
                OutboundEndpointId = Guid.CreateVersion7(),
                WebhookMessageId = Guid.CreateVersion7(),
                Attempt = 1,
                Status = WebhookDeliveryStatus.Delivered,
                ResponseCode = 200,
                NextRetryAt = null,
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookDeliveryAttemptedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OutboundEndpointId = Guid.CreateVersion7(),
                WebhookMessageId = Guid.CreateVersion7(),
                Attempt = 1,
                Status = WebhookDeliveryStatus.Failed,
                ResponseCode = 500,
                NextRetryAt = null,
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AutoDisabled_NotifiesWebhookEndpointAutoDisabled()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookEndpointAutoDisabledBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        Guid endpointId = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                OutboundEndpointId = endpointId,
                ConsecutiveFailureCount = 20,
                Reason = "consecutive_failures",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "webhook_endpoint_auto_disabled",
                Arg.Is<object>(data =>
                    data is WebhookEndpointAutoDisabledAlertDto
                    && ((WebhookEndpointAutoDisabledAlertDto)data).OutboundEndpointId
                        == endpointId.ToString()
                    && ((WebhookEndpointAutoDisabledAlertDto)data).ConsecutiveFailureCount == 20
                    && ((WebhookEndpointAutoDisabledAlertDto)data).Reason == "consecutive_failures"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AutoDisabled_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        WebhookEndpointAutoDisabledBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OutboundEndpointId = Guid.CreateVersion7(),
                ConsecutiveFailureCount = 20,
                Reason = "consecutive_failures",
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }
}
