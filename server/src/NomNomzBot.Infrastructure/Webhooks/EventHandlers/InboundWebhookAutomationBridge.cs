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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Webhooks.EventHandlers;

/// <summary>
/// The missing integration seam (webhooks.md §3.2 step 5-6): a verified inbound webhook drives the automation
/// the operator configured on its endpoint. Until now a Generic/GitHub inbound webhook was verified, journaled,
/// and then inert — only monetization providers went further (<see cref="Supporters.EventHandlers.SupporterWebhookBridge"/>).
/// This handler closes that gap: it loads the endpoint's target and routes the webhook's payload to either a
/// bound pipeline (<see cref="IPipelineEngine"/>, by <c>TargetPipelineId</c>) or an event-response
/// (<see cref="IEventResponseExecutor"/>, by <c>TargetEventType</c>). An endpoint with neither target set stays a
/// pure journal sink (e.g. a supporter-only endpoint) — this handler is a no-op for it, so it never double-fires.
/// </summary>
public sealed class InboundWebhookAutomationBridge : IEventHandler<InboundWebhookReceivedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly IEventJournal _journal;
    private readonly IPipelineEngine _pipeline;
    private readonly IEventResponseExecutor _eventResponses;
    private readonly ILogger<InboundWebhookAutomationBridge> _logger;

    public InboundWebhookAutomationBridge(
        IApplicationDbContext db,
        IEventJournal journal,
        IPipelineEngine pipeline,
        IEventResponseExecutor eventResponses,
        ILogger<InboundWebhookAutomationBridge> logger
    )
    {
        _db = db;
        _journal = journal;
        _pipeline = pipeline;
        _eventResponses = eventResponses;
        _logger = logger;
    }

    public async Task HandleAsync(
        InboundWebhookReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        // A redelivery the journal already had, or a tenant-less event, drives nothing.
        if (@event.WasDuplicate || @event.BroadcasterId == Guid.Empty)
            return;

        InboundWebhookEndpoint? endpoint = await _db
            .InboundWebhookEndpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == @event.InboundEndpointId, cancellationToken);

        // Nothing to route to → this endpoint is a pure journal/ingest sink; leave it be (no double-fire).
        if (
            endpoint is null
            || (endpoint.TargetPipelineId is null && endpoint.TargetEventType is null)
        )
            return;

        Dictionary<string, string> variables = await BuildVariablesAsync(@event, cancellationToken);

        if (endpoint.TargetPipelineId is Guid pipelineId)
        {
            await RunPipelineAsync(@event, endpoint, pipelineId, variables, cancellationToken);
            return;
        }

        // TargetEventType is set (the else branch of the null-guard above).
        await _eventResponses.ExecuteAsync(
            @event.BroadcasterId,
            endpoint.TargetEventType!,
            userId: null,
            userDisplayName: endpoint.Name,
            variables,
            cancellationToken
        );
    }

    private async Task RunPipelineAsync(
        InboundWebhookReceivedEvent @event,
        InboundWebhookEndpoint endpoint,
        Guid pipelineId,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _pipeline.ExecuteAsync(
                new PipelineRequest
                {
                    BroadcasterId = @event.BroadcasterId,
                    // The engine loads PipelineStep rows for this id (its preferred path); no inline graph needed.
                    PipelineId = pipelineId,
                    TriggeredByUserId = "webhook",
                    TriggeredByDisplayName = endpoint.Name,
                    InitialVariables = variables,
                },
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A pipeline fault never propagates back into the event bus (bookkeeping must not break ingest).
            _logger.LogWarning(
                ex,
                "Inbound webhook {EventType} failed to run pipeline {PipelineId} on {Channel}.",
                @event.EventType,
                pipelineId,
                @event.BroadcasterId
            );
        }
    }

    // Reconstructs the template variables from the journaled payload (the adapter's flattened bag), namespaced
    // under payload.* (webhooks.md §7 — the tainted external namespace), plus webhook.* metadata about the source.
    private async Task<Dictionary<string, string>> BuildVariablesAsync(
        InboundWebhookReceivedEvent @event,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook.event_type"] = @event.EventType,
            ["webhook.provider"] = @event.Adapter.ToString().ToLowerInvariant(),
            ["webhook.provider_event_id"] = @event.ProviderEventId,
        };

        Result<EventRecord> record = await _journal.GetByEventIdAsync(
            @event.JournalEventId,
            cancellationToken
        );
        if (record.IsFailure)
            return variables;

        foreach ((string key, string value) in ParseFlatPayload(record.Value.PayloadJson))
            variables[$"payload.{key}"] = value;

        return variables;
    }

    // The dispatcher journals the parsed event's flat string→string variable bag; deserialize it back. A payload
    // that isn't a flat object (unexpected) yields no payload.* variables rather than throwing.
    private static IReadOnlyDictionary<string, string> ParseFlatPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, string>();
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(payloadJson)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
