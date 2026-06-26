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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Base class for stream engagement event handlers.
/// Looks up the operator-configured <see cref="EventResponse"/> for this event type and executes it:
/// <list type="bullet">
///   <item><description><c>chat_message</c> — resolves template variables and sends a chat message.</description></item>
///   <item><description><c>pipeline</c> — runs the bound pipeline via <see cref="IPipelineEngine"/>.</description></item>
///   <item><description><c>none</c> (or no config) — does nothing.</description></item>
/// </list>
/// Also logs the event to <c>ChannelEvents</c> so the activity feed and analytics projections pick it up.
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

        // Look up the operator's configured response for this event type.
        NomNomzBot.Domain.Commands.Entities.EventResponse? config =
            await db.EventResponses.FirstOrDefaultAsync(
                r => r.BroadcasterId == broadcasterId && r.EventType == EventTypeKey && r.IsEnabled,
                ct
            );

        if (config is null)
            return;

        Dictionary<string, string> variables = BuildVariables(@event);

        Logger.LogDebug(
            "Executing event_response:{EventType} ({ResponseType}) for channel {Channel}",
            EventTypeKey,
            config.ResponseType,
            broadcasterId
        );

        try
        {
            switch (config.ResponseType)
            {
                case "chat_message":
                    await SendChatMessageAsync(
                        scope,
                        db,
                        broadcasterId,
                        config.Message,
                        variables,
                        ct
                    );
                    break;

                case "pipeline":
                    if (config.PipelineId.HasValue)
                    {
                        NomNomzBot.Domain.Commands.Entities.Pipeline? pipeline =
                            await db.Pipelines.FirstOrDefaultAsync(
                                p => p.Id == config.PipelineId.Value,
                                ct
                            );
                        if (pipeline is not null)
                        {
                            await Pipeline.ExecuteAsync(
                                new()
                                {
                                    BroadcasterId = broadcasterId,
                                    PipelineId = config.PipelineId,
                                    PipelineJson = pipeline.GraphJsonCache ?? "{}",
                                    TriggeredByUserId = GetUserId(@event) ?? string.Empty,
                                    TriggeredByDisplayName =
                                        GetUserDisplayName(@event) ?? string.Empty,
                                    RawMessage = string.Empty,
                                    InitialVariables = variables,
                                },
                                ct
                            );
                        }
                    }
                    break;

                // "none" or any unknown type: no action.
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to execute event_response:{EventType} ({ResponseType}) in {Channel}",
                EventTypeKey,
                config.ResponseType,
                broadcasterId
            );
        }
    }

    private async Task SendChatMessageAsync(
        IServiceScope scope,
        IApplicationDbContext db,
        Guid broadcasterId,
        string? messageTemplate,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(messageTemplate))
            return;

        ITemplateResolver templateResolver =
            scope.ServiceProvider.GetRequiredService<ITemplateResolver>();
        IChatProvider chatProvider = scope.ServiceProvider.GetRequiredService<IChatProvider>();

        string message = await templateResolver.ResolveAsync(
            messageTemplate,
            variables,
            broadcasterId,
            ct
        );

        if (!string.IsNullOrWhiteSpace(message))
            await chatProvider.SendMessageAsync(broadcasterId, message, ct);
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
