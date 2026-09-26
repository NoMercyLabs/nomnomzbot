// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Outbound webhook endpoints whose deliveries fail. An endpoint the dispatcher auto-disabled after too many
/// consecutive failures (<see cref="OutboundWebhookEndpoint.DisabledAt"/> set, only the dispatcher sets it) is
/// critical: nothing is delivered until the streamer fixes and re-enables it. An enabled endpoint in a failure
/// streak of <see cref="FailingStreakThreshold"/> or more is a warning. The keys embed the disable instant and
/// the last success, so a new disable or a new streak after a recovery surfaces again.
/// </summary>
public sealed class OutboundWebhookFailureSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string DisabledKeyPrefix = "webhook-disabled:";
    private const string FailingKeyPrefix = "webhook-failing:";

    /// <summary>
    /// A single failed attempt is retried with backoff and usually recovers on its own; three in a row means the
    /// receiver is really down or misconfigured, which is when the streamer has something to fix.
    /// </summary>
    public const int FailingStreakThreshold = 3;

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [DisabledKeyPrefix, FailingKeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(OutboundWebhookAttemptedEvent),
        nameof(OutboundWebhookAutoDisabledEvent),
        nameof(ChannelConfigChangedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<OutboundWebhookEndpoint> endpoints = await db
            .OutboundWebhookEndpoints.IgnoreQueryFilters()
            .Where(e =>
                e.BroadcasterId == channelId
                && e.DeletedAt == null
                && (
                    (!e.IsEnabled && e.DisabledAt != null)
                    || (e.IsEnabled && e.ConsecutiveFailureCount >= FailingStreakThreshold)
                )
            )
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items =
        [
            .. endpoints.Select(ToItem).Where(item => !dismissedKeys.Contains(item.Id)),
        ];
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(OutboundWebhookEndpoint endpoint)
    {
        Dictionary<string, string> parameters = new()
        {
            ["endpointName"] = endpoint.Name,
            ["failureCount"] = endpoint.ConsecutiveFailureCount.ToString(),
        };

        if (endpoint is { IsEnabled: false, DisabledAt: { } disabledAt })
            return new ActionRequiredItemDto(
                Id: $"{DisabledKeyPrefix}{endpoint.Id}:{disabledAt.Ticks}",
                Kind: "webhook_endpoint_disabled",
                Severity: "critical",
                TitleKey: "attention_webhook_disabled_title",
                MessageKey: "attention_webhook_disabled_message",
                Parameters: parameters,
                DetectedAt: disabledAt,
                DeepLinkRoute: "webhooks",
                SourceUserId: null,
                SourceUserName: null,
                Count: 1,
                QueueItemIds: []
            );

        DateTime streakStart = endpoint.LastSuccessAt ?? endpoint.CreatedAt;
        return new ActionRequiredItemDto(
            Id: $"{FailingKeyPrefix}{endpoint.Id}:{streakStart.Ticks}",
            Kind: "webhook_deliveries_failing",
            Severity: "warning",
            TitleKey: "attention_webhook_failing_title",
            MessageKey: "attention_webhook_failing_message",
            Parameters: parameters,
            DetectedAt: endpoint.LastDeliveryAt ?? streakStart,
            DeepLinkRoute: "webhooks",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );
    }
}
