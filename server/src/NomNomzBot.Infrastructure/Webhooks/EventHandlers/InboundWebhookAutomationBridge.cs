// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Webhooks.EventHandlers;

/// <summary>
/// The missing integration seam (webhooks.md §3.2 step 5-6): a verified inbound webhook drives the automation
/// the operator configured on its endpoint. Until now a Generic/GitHub inbound webhook was verified, journaled,
/// and then inert — only monetization providers went further (<see cref="Supporters.EventHandlers.SupporterWebhookBridge"/>).
/// This handler closes that gap: it loads the endpoint's target and routes the webhook's payload to either a
/// bound target (a pipeline, an action or a command, run through <see cref="ICommandInvoker"/>) or an event-response
/// (<see cref="IEventResponseExecutor"/>, by <c>TargetEventType</c>). An endpoint with neither target set stays a
/// pure journal sink (e.g. a supporter-only endpoint) — this handler is a no-op for it, so it never double-fires.
/// </summary>
public sealed class InboundWebhookAutomationBridge : IEventHandler<InboundWebhookReceivedEvent>
{
    private readonly IApplicationDbContext _db;
    private readonly IEventJournal _journal;
    private readonly ICommandInvoker _invoker;
    private readonly IEventResponseExecutor _eventResponses;
    private readonly ILogger<InboundWebhookAutomationBridge> _logger;

    private const string TargetTriggerSource = "webhook";

    public InboundWebhookAutomationBridge(
        IApplicationDbContext db,
        IEventJournal journal,
        ICommandInvoker invoker,
        IEventResponseExecutor eventResponses,
        ILogger<InboundWebhookAutomationBridge> logger
    )
    {
        _db = db;
        _journal = journal;
        _invoker = invoker;
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

        BindingTarget? target = endpoint?.GetBindingTarget();

        // Nothing to route to → this endpoint is a pure journal/ingest sink; leave it be (no double-fire).
        if (endpoint is null || (target is null && endpoint.TargetEventType is null))
            return;

        Dictionary<string, string> variables = await BuildVariablesAsync(@event, cancellationToken);

        if (target is not null)
        {
            await RunTargetAsync(@event, endpoint, target, variables, cancellationToken);
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

    // Runs the endpoint's one target (a pipeline, an action or a command) as the channel's system invoker, with
    // the payload variables. A failing target is logged and never propagates into the event bus.
    private async Task RunTargetAsync(
        InboundWebhookReceivedEvent @event,
        InboundWebhookEndpoint endpoint,
        BindingTarget target,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken
    )
    {
        InvocationContext context = new(
            UserId: null,
            DisplayName: endpoint.Name,
            Args: string.Empty,
            TriggerSource: TargetTriggerSource,
            Variables: variables
        );

        try
        {
            Result<InvocationOutcome> outcome = await _invoker.InvokeAsync(
                @event.BroadcasterId,
                target,
                context,
                cancellationToken
            );
            if (outcome.IsFailure)
                _logger.LogWarning(
                    "Inbound webhook {EventType} could not run its {Kind} target on {Channel}: {Error}",
                    @event.EventType,
                    target.Kind,
                    @event.BroadcasterId,
                    outcome.ErrorMessage
                );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A target fault never propagates back into the event bus (bookkeeping must not break ingest).
            _logger.LogWarning(
                ex,
                "Inbound webhook {EventType} failed to run its {Kind} target on {Channel}.",
                @event.EventType,
                target.Kind,
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
        Result<EventRecord> record = await _journal.GetByEventIdAsync(
            @event.JournalEventId,
            cancellationToken
        );

        return InboundWebhookVariables.Build(
            @event.EventType,
            @event.Adapter,
            @event.ProviderEventId,
            record.IsFailure ? [] : ParseFlatPayload(record.Value.PayloadJson)
        );
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
