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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing;

namespace NomNomzBot.Infrastructure.Dashboard.Replay;

/// <summary>
/// <see cref="IActivityReplayService"/> over the event journal. Each step of the chain is rebuilt from its
/// journal row and handed to the alert handler's own presentation half (<see cref="IEventResponsePresenter"/> —
/// the same variables and response key the live event used, through the same <see cref="IEventResponseExecutor"/>),
/// then its captured overlay alerts are re-sent. Steps run one after another in their original order.
///
/// What this never does, by construction: publish the event on the bus, call a handler's
/// <c>HandleAsync</c> (which logs the activity row and runs the handler's other work, e.g. the watch-streak
/// upsert), or reach any currency, loyalty, reward, counter, stats or hype-train service.
/// </summary>
public sealed class ActivityReplayService : IActivityReplayService
{
    private readonly IApplicationDbContext _db;
    private readonly JournaledDomainEventReader _reader;
    private readonly GiftBombChainResolver _giftBombs;
    private readonly IReadOnlyDictionary<Type, IEventResponsePresenter> _presenters;
    private readonly IRenderedAlertReplayer _capturedAlerts;

    public ActivityReplayService(
        IApplicationDbContext db,
        JournaledDomainEventReader reader,
        GiftBombChainResolver giftBombs,
        IEnumerable<IEventResponsePresenter> presenters,
        IRenderedAlertReplayer capturedAlerts
    )
    {
        _db = db;
        _reader = reader;
        _giftBombs = giftBombs;
        _presenters = presenters.ToDictionary(p => p.EventType);
        _capturedAlerts = capturedAlerts;
    }

    public async Task<Result<ActivityReplayResult>> ReplayAsync(
        Guid broadcasterId,
        string channelEventId,
        CancellationToken ct = default
    )
    {
        List<ActivityReplayItem> chain = await ResolveChainAsync(broadcasterId, channelEventId, ct);

        EventResponseOutcome responses = EventResponseOutcome.None;
        int widgetsNotified = 0;
        foreach (ActivityReplayItem item in chain)
        {
            EventResponseOutcome? replayed = await ReplayResponseAsync(item.Event, ct);
            if (replayed is not null)
                responses = responses.Plus(replayed);

            // A replayed response queues its own TTS, so the captured utterance is skipped then.
            widgetsNotified += await _capturedAlerts.ResendAsync(
                broadcasterId,
                item.ChannelEventId,
                includeTts: replayed is null,
                ct
            );
        }

        bool rebuilt = chain.Any(i => i.Event is not null);
        if (!rebuilt && widgetsNotified == 0)
            return Result.Failure<ActivityReplayResult>(
                "This activity event has no stored event and no captured alert — nothing to replay.",
                "NOT_FOUND"
            );

        return Result.Success(
            new ActivityReplayResult(
                chain.Count,
                responses.ChatMessagesSent,
                responses.TtsQueued,
                responses.OverlaysShown,
                widgetsNotified
            )
        );
    }

    /// <summary>The configured response replayed through the event type's alert handler; null when the type has
    /// none (the captured alerts are then the whole replay).</summary>
    private async Task<EventResponseOutcome?> ReplayResponseAsync(
        IDomainEvent? domainEvent,
        CancellationToken ct
    ) =>
        domainEvent is not null
        && _presenters.TryGetValue(domainEvent.GetType(), out IEventResponsePresenter? presenter)
            ? await presenter.ReplayAsync(domainEvent, ct)
            : null;

    /// <summary>The event itself, followed — for a gift bomb — by each named recipient in announcement order.</summary>
    private async Task<List<ActivityReplayItem>> ResolveChainAsync(
        Guid broadcasterId,
        string channelEventId,
        CancellationToken ct
    )
    {
        (IDomainEvent Event, DateTime OccurredAt)? stored = await ReadStoredEventAsync(
            broadcasterId,
            channelEventId,
            ct
        );
        if (stored is null)
            return [new(channelEventId, null)];

        List<ActivityReplayItem> chain = [new(channelEventId, stored.Value.Event)];
        if (stored.Value.Event is GiftSubscriptionEvent bomb)
            chain.AddRange(await _giftBombs.RecipientsOfAsync(bomb, stored.Value.OccurredAt, ct));

        return chain;
    }

    // An activity row's id IS the domain event's EventId (TwitchAlertHandlerBase and the channel-event projection
    // both key it so), which the journal keeps as EventJournal.EventId.
    private async Task<(IDomainEvent Event, DateTime OccurredAt)?> ReadStoredEventAsync(
        Guid broadcasterId,
        string channelEventId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelEventId, out Guid eventId))
            return null;

        List<EventJournal> rows = await _db
            .EventJournals.AsNoTracking()
            .Where(e => e.BroadcasterId == broadcasterId && e.EventId == eventId)
            .ToListAsync(ct);

        foreach (EventJournal row in rows)
        {
            Result<IDomainEvent> read = await _reader.ReadAsync(row, ct);
            if (read.IsSuccess)
                return (read.Value, row.OccurredAt);
        }

        return null;
    }
}
