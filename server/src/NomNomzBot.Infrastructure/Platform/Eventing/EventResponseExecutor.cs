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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// <see cref="IEventResponseExecutor"/> over the tenant's <see cref="EventResponse"/> rows:
/// <c>chat_message</c> resolves the operator's template against the trigger's variables and sends it via
/// the chat provider; <c>pipeline</c> runs the bound pipeline's cached graph with the variables seeded;
/// <c>none</c> (or a disabled/absent row) does nothing. Scoped — trigger sources resolve it from their
/// own scope (hosted-service handlers) or take it by constructor (already-scoped handlers).
/// </summary>
public sealed class EventResponseExecutor : IEventResponseExecutor
{
    private readonly IApplicationDbContext _db;
    private readonly IPipelineEngine _pipeline;
    private readonly ITemplateResolver _templateResolver;
    private readonly IChatProvider _chatProvider;
    private readonly ILogger<EventResponseExecutor> _logger;

    public EventResponseExecutor(
        IApplicationDbContext db,
        IPipelineEngine pipeline,
        ITemplateResolver templateResolver,
        IChatProvider chatProvider,
        ILogger<EventResponseExecutor> logger
    )
    {
        _db = db;
        _pipeline = pipeline;
        _templateResolver = templateResolver;
        _chatProvider = chatProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default
    )
    {
        EventResponse? config = await _db.EventResponses.FirstOrDefaultAsync(
            r => r.BroadcasterId == broadcasterId && r.EventType == eventTypeKey && r.IsEnabled,
            cancellationToken
        );
        if (config is null)
            return;

        _logger.LogDebug(
            "Executing event response {EventType} ({ResponseType}) for channel {Channel}",
            eventTypeKey,
            config.ResponseType,
            broadcasterId
        );

        try
        {
            switch (config.ResponseType)
            {
                case "chat_message":
                    await SendChatMessageAsync(
                        broadcasterId,
                        config.Message,
                        variables,
                        cancellationToken
                    );
                    break;

                case "pipeline":
                    await RunPipelineAsync(
                        broadcasterId,
                        config.PipelineId,
                        userId,
                        userDisplayName,
                        variables,
                        cancellationToken
                    );
                    break;

                // "none" or any unknown type: no action.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute event response {EventType} ({ResponseType}) in {Channel}",
                eventTypeKey,
                config.ResponseType,
                broadcasterId
            );
        }
    }

    private async Task SendChatMessageAsync(
        Guid broadcasterId,
        string? messageTemplate,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(messageTemplate))
            return;

        string message = await _templateResolver.ResolveAsync(
            messageTemplate,
            variables,
            broadcasterId,
            ct
        );

        if (!string.IsNullOrWhiteSpace(message))
            await _chatProvider.SendMessageAsync(broadcasterId, message, ct);
    }

    private async Task RunPipelineAsync(
        Guid broadcasterId,
        Guid? pipelineId,
        string? userId,
        string? userDisplayName,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        if (!pipelineId.HasValue)
            return;

        Domain.Commands.Entities.Pipeline? pipeline = await _db.Pipelines.FirstOrDefaultAsync(
            p => p.Id == pipelineId.Value,
            ct
        );
        if (pipeline is null)
            return;

        await _pipeline.ExecuteAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                PipelineId = pipelineId,
                PipelineJson = pipeline.GraphJsonCache ?? "{}",
                TriggeredByUserId = userId ?? string.Empty,
                TriggeredByDisplayName = userDisplayName ?? string.Empty,
                RawMessage = string.Empty,
                InitialVariables = variables,
            },
            ct
        );
    }
}
