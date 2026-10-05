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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Base class for stream engagement event handlers: builds the event's template variables, logs the event
/// to <c>ChannelEvents</c> (so the activity feed and analytics projections pick it up), and dispatches the
/// operator-configured response through <see cref="IEventResponseExecutor"/> — the one execution path all
/// trigger sources share. The response half is also an <see cref="IEventResponsePresenter"/>, so a dashboard
/// replay runs the exact same variables and response key without logging or re-running anything else.
/// </summary>
public abstract class TwitchAlertHandlerBase<TEvent> : IEventResponsePresenter, ITriggerSampleSource
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

    /// <summary>
    /// The event-response key whose configured response runs for this event. Defaults to <see cref="EventTypeKey"/>
    /// (which still names the logged <c>ChannelEvents</c> row); a handler overrides it when one event needs a
    /// differently-worded response — e.g. an anonymous gifter — so that wording stays an editable template.
    /// </summary>
    protected virtual string ResponseKeyFor(TEvent @event) => EventTypeKey;

    protected abstract string? GetUserId(TEvent @event);
    protected abstract string? GetUserDisplayName(TEvent @event);
    protected abstract Dictionary<string, string> BuildVariables(TEvent @event);

    /// <summary>A realistic instance of the event, for a test run.</summary>
    protected abstract TEvent SampleEvent(DateTimeOffset now);

    public TriggerSample Sample(DateTimeOffset now)
    {
        ResponseTrigger trigger = TriggerFor(SampleEvent(now));
        return new(
            typeof(TEvent).Name,
            trigger.ResponseKey,
            trigger.UserId,
            trigger.UserDisplayName,
            trigger.Variables
        );
    }

    /// <summary>Renders a seconds count as chat-friendly text ("3 minutes", "45 seconds") instead of a raw
    /// number — shared by every handler that seeds a duration-typed template variable (ad breaks, timeouts,
    /// polls). Twitch-sourced durations run in whole minutes far more often than not, so a clean minute count
    /// only falls back to seconds when the value doesn't divide evenly.</summary>
    protected static string HumanDuration(int seconds)
    {
        if (seconds >= 60 && seconds % 60 == 0)
        {
            int minutes = seconds / 60;
            return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
        }

        return $"{seconds} second{(seconds == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Whether this event gets announced at all. Defaults to always; a handler overrides it when some events of
    /// its type must stay silent (e.g. a gifted sub's recipient-side notice). The live path and a replay both
    /// honour it, so a replay never says something the stream never heard.
    /// </summary>
    protected virtual bool Announces(TEvent @event) => true;

    /// <summary>
    /// Whether this event may speak in chat. Unlike <see cref="Announces"/> it still logs the event; a handler
    /// overrides it when only some events of its type get a response (a poll that was cut short stays silent).
    /// </summary>
    protected virtual bool Responds(TEvent @event) => true;

    public Type EventType => typeof(TEvent);

    protected async Task HandleCoreAsync(TEvent @event, CancellationToken ct)
    {
        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty || !Announces(@event))
            return;

        using IServiceScope scope = ScopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        await LogChannelEventAsync(db, @event, broadcasterId, ct);
        if (!Responds(@event))
            return;

        // THE one execution path for configured responses — shared with every other trigger source.
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        ResponseTrigger trigger = TriggerFor(@event);
        await executor.ExecuteAsync(
            broadcasterId,
            trigger.ResponseKey,
            trigger.UserId,
            trigger.UserDisplayName,
            trigger.Variables,
            ct
        );
    }

    public async Task<EventResponseOutcome> ReplayAsync(IDomainEvent @event, CancellationToken ct)
    {
        if (
            @event is not TEvent typed
            || typed.BroadcasterId == Guid.Empty
            || !Announces(typed)
            || !Responds(typed)
        )
            return EventResponseOutcome.None;

        using IServiceScope scope = ScopeFactory.CreateScope();
        IEventResponseExecutor executor =
            scope.ServiceProvider.GetRequiredService<IEventResponseExecutor>();
        ResponseTrigger trigger = TriggerFor(typed);
        return await executor.ReplayAsync(
            typed.BroadcasterId,
            trigger.ResponseKey,
            trigger.UserId,
            trigger.UserDisplayName,
            trigger.Variables,
            ct
        );
    }

    private ResponseTrigger TriggerFor(TEvent @event) =>
        new(
            ResponseKeyFor(@event),
            GetUserId(@event),
            GetUserDisplayName(@event),
            SeedTargetAlias(BuildVariables(@event))
        );

    /// <summary>Everything the executor needs for one event — built once, so live and replay never drift.</summary>
    private sealed record ResponseTrigger(
        string ResponseKey,
        string? UserId,
        string? UserDisplayName,
        Dictionary<string, string> Variables
    );

    /// <summary>
    /// {target.*} is documented (and, on the command path, wired via <c>ChatMessageHandler
    /// .BuildInitialVariables</c>) to resolve from a command argument — e.g. <c>!so &lt;name&gt;</c> seeds
    /// <c>target</c> from <c>{args.1}</c>. An event-response pipeline (raid/follow/sub/…) never has a
    /// command argument, so a shoutout (or any other action) referencing <c>{target.name}</c> in an
    /// automated event pipeline silently resolved empty. Since every built-in event trigger already has
    /// exactly one obvious subject — the triggering user — <c>target</c> transparently aliases to <c>user</c>
    /// here when the handler didn't set one explicitly, so <c>{target}</c> works the same way in both a
    /// command and an event-response pipeline without the operator needing to know the distinction.
    /// </summary>
    private static Dictionary<string, string> SeedTargetAlias(Dictionary<string, string> variables)
    {
        if (!variables.ContainsKey("target") && variables.TryGetValue("user", out string? user))
            variables["target"] = user;
        if (
            !variables.ContainsKey("target.id")
            && variables.TryGetValue("user.id", out string? userId)
        )
            variables["target.id"] = userId;
        if (
            !variables.ContainsKey("target.name")
            && variables.TryGetValue("user.name", out string? userName)
        )
            variables["target.name"] = userName;
        if (
            !variables.ContainsKey("target.link")
            && variables.TryGetValue("user.link", out string? userLink)
        )
            variables["target.link"] = userLink;

        return variables;
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
