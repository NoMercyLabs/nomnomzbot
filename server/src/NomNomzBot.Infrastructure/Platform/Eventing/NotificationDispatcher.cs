// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Turns one raw EventSub notification into a journaled, deduped, fanned-out fact (twitch-eventsub §3.4). This
/// is the GENERIC dispatch: it routes by <c>subscription_type</c> and persists the raw event payload so no
/// event is lost — the strongly-typed per-topic parsing/handlers (the 74-event fan-out) are deferred to the
/// fan-out subsystem, which reads the journal / subscribes to the journaled event emitted here.
/// <para>
/// Dedupe is the journal's <c>Unique(EventId)</c>: the message-id derives the <c>EventId</c> via UUIDv5
/// (<see cref="EventSubMessageId"/>), so a redelivery resolves to the already-stored row and consumes no new
/// stream position. The pre-check makes the duplicate observable (<c>WasDuplicate</c>) without a second append.
/// </para>
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEventJournal _journal;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEventJournal journal,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<NotificationDispatcher> logger
    )
    {
        _journal = journal;
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<NotificationDispatchResult>> DispatchAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        Guid eventId = EventSubMessageId.ForMessageId(notification.MessageId);

        // Dedupe: a redelivery of the same message-id maps to the same EventId — the journal already has it.
        Result<EventRecord> existing = await _journal.GetByEventIdAsync(eventId, ct);
        if (existing.IsSuccess)
        {
            await PublishJournaledAsync(notification, existing.Value, wasDuplicate: true, ct);
            return Result.Success(
                new NotificationDispatchResult(eventId, existing.Value.StreamPosition, true)
            );
        }

        // Journal the raw event payload, typed by subscription_type + version. The append is itself idempotent
        // on EventId, so a concurrent redelivery still collapses to one row (returned without a new position).
        AppendEventRequest append = new(
            EventId: eventId,
            BroadcasterId: notification.BroadcasterId,
            EventType: notification.SubscriptionType,
            EventVersion: ParseVersion(notification.SubscriptionVersion),
            Source: "eventsub",
            PayloadJson: notification.Event.GetRawText(),
            MetadataJson: BuildMetadata(notification),
            OccurredAt: notification.MessageTimestamp.UtcDateTime,
            ActorTwitchUserId: notification.TwitchBroadcasterUserId
        );

        Result<EventRecord> appended = await _journal.AppendAsync(append, ct);
        if (appended.IsFailure)
        {
            _logger.LogError(
                "EventSub dispatch: journal append failed for {Type} ({Code})",
                notification.SubscriptionType,
                appended.ErrorCode
            );
            return Result.Failure<NotificationDispatchResult>(
                appended.ErrorMessage!,
                appended.ErrorCode,
                appended.ErrorDetail
            );
        }

        // We reached the append because the pre-check found no existing row, so this is the genuinely-new
        // path. (A concurrent redelivery is still safe: the journal's Unique(EventId) collapses it to one row;
        // the next delivery's pre-check then observes it as the duplicate.)
        await PublishJournaledAsync(notification, appended.Value, wasDuplicate: false, ct);

        return Result.Success(
            new NotificationDispatchResult(
                eventId,
                appended.Value.StreamPosition,
                WasDuplicate: false
            )
        );
    }

    private Task PublishJournaledAsync(
        EventSubNotification notification,
        EventRecord record,
        bool wasDuplicate,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new EventSubNotificationJournaledEvent
            {
                BroadcasterId = notification.BroadcasterId,
                JournalEventId = record.EventId,
                StreamPosition = record.StreamPosition,
                EventType = notification.SubscriptionType,
                WasDuplicate = wasDuplicate,
                Timestamp = _clock.GetUtcNow(),
            },
            ct
        );

    private static int ParseVersion(string version) =>
        int.TryParse(version, out int parsed) ? parsed : 1;

    private static string BuildMetadata(EventSubNotification notification) =>
        $$"""
        {"message_id":{{Quote(notification.MessageId)}},"subscription_type":{{Quote(
            notification.SubscriptionType
        )}},"subscription_version":{{Quote(notification.SubscriptionVersion)}}}
        """;

    private static string Quote(string value) => Newtonsoft.Json.JsonConvert.ToString(value);
}
