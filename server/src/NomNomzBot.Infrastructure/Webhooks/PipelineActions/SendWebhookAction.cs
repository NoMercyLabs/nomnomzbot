// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;

namespace NomNomzBot.Infrastructure.Webhooks.PipelineActions;

/// <summary>
/// Pipeline action <c>send_webhook</c> (webhooks.md §6). The explicit outbound counterpart to event-subscription
/// fan-out: a pipeline POSTs the current variable bag to ONE of the channel's outbound endpoints on demand. The
/// endpoint renders its own body template + Standard-Webhooks signature and rides the SSRF-hardened egress client,
/// so this action only names the endpoint and hands over the variables — it never builds a raw URL or body itself.
/// Fails (with the enqueue reason) when the endpoint id is missing/invalid or the endpoint is disabled/unknown, so
/// the pipeline log tells the truth instead of silently dropping the send.
/// </summary>
public sealed class SendWebhookAction : ICommandAction
{
    private readonly IOutboundWebhookDispatcher _dispatcher;

    public SendWebhookAction(IOutboundWebhookDispatcher dispatcher) => _dispatcher = dispatcher;

    public string ActionType => "send_webhook";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? endpointRaw = action.GetString("endpoint") ?? action.GetString("endpoint_id");
        if (
            string.IsNullOrWhiteSpace(endpointRaw)
            || !Guid.TryParse(endpointRaw, out Guid endpointId)
        )
            return ActionResult.Failure(
                "send_webhook requires an 'endpoint' parameter (an outbound webhook endpoint id)."
            );

        // The event type is metadata the receiver sees + the endpoint's subscription can match; default it so a
        // bare send_webhook still carries a stable, self-describing type.
        string eventType =
            action.GetString("event_type") is string et && !string.IsNullOrWhiteSpace(et)
                ? et
                : "pipeline.send_webhook";

        Result<OutboundEnqueueResult> result = await _dispatcher.EnqueueForEndpointAsync(
            ctx.BroadcasterId,
            endpointId,
            eventType,
            ctx.Variables,
            journalEventId: null,
            ctx.CancellationToken
        );

        return result.IsFailure
            ? ActionResult.Failure(
                result.ErrorMessage ?? "send_webhook failed to enqueue delivery."
            )
            : ActionResult.Success($"send_webhook:{endpointId}");
    }
}
