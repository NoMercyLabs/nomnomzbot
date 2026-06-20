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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Base class for stream engagement event handlers.
/// Logs the event to ChannelEvents and executes the user-configured pipeline
/// stored in Records with RecordType = "event_response:{eventType}".
/// If no config exists, does nothing (no hardcoded behavior).
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

        Record? config = await db.Records.FirstOrDefaultAsync(
            r =>
                r.BroadcasterId == broadcasterId
                && r.RecordType == $"event_response:{EventTypeKey}",
            ct
        );

        if (config is null || string.IsNullOrWhiteSpace(config.Data))
            return;

        Dictionary<string, string> variables = BuildVariables(@event);

        Logger.LogDebug(
            "Executing event_response:{EventType} pipeline for channel {Channel}",
            EventTypeKey,
            broadcasterId
        );

        try
        {
            await Pipeline.ExecuteAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    PipelineJson = config.Data,
                    // TriggeredByUserId is a Twitch string id (or empty for channel-scoped events with no user).
                    TriggeredByUserId = GetUserId(@event) ?? string.Empty,
                    TriggeredByDisplayName = GetUserDisplayName(@event) ?? string.Empty,
                    RawMessage = string.Empty,
                    InitialVariables = variables,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to execute event_response:{EventType} pipeline in {Channel}",
                EventTypeKey,
                broadcasterId
            );
        }
    }

    private async Task LogChannelEventAsync(
        IApplicationDbContext db,
        TEvent @event,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
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
                    Id = Ulid.NewUlid().ToString(),
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
