// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Base class for stream engagement event handlers: builds the event's template variables, logs the event
/// to <c>ChannelEvents</c> (so the activity feed and analytics projections pick it up), and dispatches the
/// operator-configured response through <see cref="IEventResponseExecutor"/> — the one execution path all
/// trigger sources share.
/// </summary>
public abstract class TwitchAlertHandlerBase<TEvent>
    where TEvent : class, IDomainEvent
{
    protected abstract string EventTypeKey { get; }

    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly IPipelineEngine Pipeline;
    protected readonly ILogger Logger;

    protected TwitchAlertHandlerBase(
        IServiceScopeFactory scopeFactory,
        IPipelineEngine pipeline,
        ILogger logger
    )
    {
        ScopeFactory = scopeFactory;
        Pipeline = pipeline;
        Logger = logger;
    }

    protected abstract string? GetUserId(TEvent @event);
    protected abstract string? GetUserDisplayName(TEvent @event);
    protected abstract Dictionary<string, string> BuildVariables(TEvent @event);

    protected async Task HandleCoreAsync(TEvent @event, CancellationToken ct)
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty)
            return;

        using IServiceScope scope = ScopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        await LogChannelEventAsync(db, @event, broadcasterId, ct);

        // THE one execution path for configured responses — shared with every other trigger source.
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        await executor.ExecuteAsync(
            broadcasterId,
            EventTypeKey,
            GetUserId(@event),
            GetUserDisplayName(@event),
            BuildVariables(@event),
            ct
        );
    }

    // protected (not private) so the id-convergence + idempotency behavior can be unit-tested in isolation
    // against a ChannelEvents+Users context without wiring the full event-response execution path.
    protected async Task LogChannelEventAsync(
        IApplicationDbContext db,
        TEvent @event,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            // Key the row by the domain event's EventId — the SAME id TwitchChannelEventLogProjection uses (the
            // journal preserves it as EventRecord.EventId). This collapses the instant alert-handler write and the
            // later projection enrichment into ONE ChannelEvents row: the handler writes first (resolved UserId +
            // alert variables), the projection then folds its richer Data onto the same row. A fresh id here made
            // every alert event show up TWICE in the activity feed. Idempotent: if the projection (or an EventSub
            // re-delivery) already logged this EventId, skip — no duplicate, no spurious error log.
            string eventId = @event.EventId.ToString();
            if (await db.ChannelEvents.AnyAsync(e => e.Id == eventId, ct))
                return;

            Dictionary<string, string> variables = BuildVariables(@event);

            // GetUserId returns the Twitch string id; ChannelEvent.UserId is the internal Users.Id Guid FK,
            // so resolve it (null when the event has no user or the user is not yet persisted).
            string? twitchUserId = GetUserId(@event);
            Guid? userId = twitchUserId is null
                ? null
                : await db
                    .Users.Where(u => u.TwitchUserId == twitchUserId)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(ct);

            db.ChannelEvents.Add(
                new()
                {
                    Id = eventId,
                    ChannelId = broadcasterId,
                    UserId = userId,
                    Type = EventTypeKey,
                    Data = JsonSerializer.Serialize(variables),
                }
            );
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to log ChannelEvent {EventType} for {Channel}",
                EventTypeKey,
                broadcasterId
            );
        }
    }
}
