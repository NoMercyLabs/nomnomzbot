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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Post-commit hook that fans-out to every enabled <see cref="OutboundWebhookEndpoint"/> subscribed to the
/// journaled event type (webhooks.md §9). Runs after the journal row commits, before live bus handlers —
/// synchronously, as part of the caller's <c>PublishAsync</c> (<c>JournalingEventBusDecorator</c> awaits every
/// hook). The actual HTTP delivery can be slow or hang, so it is detached onto the thread pool with its own
/// DI scope (S099a — mirrors <c>EventBus.PublishFireAndForget</c>'s <c>Task.Run</c> pattern) instead of being
/// awaited here: the event raiser must never block on a third party's webhook endpoint. The detached delivery's
/// <see cref="Result"/> is still observed — a failure is logged, never silently dropped — even though the
/// caller of this method has already moved on. Idempotent: <see cref="EventRecord.EventId"/> is the dedupe key
/// inside the dispatcher.
/// </summary>
public sealed class OutboundWebhookFanoutHandler(
    IApplicationDbContext db,
    ILogger<OutboundWebhookFanoutHandler> logger,
    IServiceScopeFactory scopeFactory
) : IJournalPostCommitHook
{
    public async Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    )
    {
        if (committed.BroadcasterId is not { } broadcasterId)
            return Result.Success();

        // Fast-skip: bail out immediately if there are no enabled endpoints for this tenant. The dispatcher
        // applies per-endpoint event-type filtering; we just need to know if fanout is worth attempting.
        bool hasEndpoints = await db.OutboundWebhookEndpoints.AnyAsync(
            e => e.BroadcasterId == broadcasterId && e.IsEnabled && e.DeletedAt == null,
            cancellationToken
        );

        if (!hasEndpoints)
            return Result.Success();

        // Build a flat variable map from the event payload so templates can reference {{event.*}}.
        IReadOnlyDictionary<string, string> variables = BuildVariables(committed);

        // Detached: a fresh scope (its own DbContext/HttpClient) so the delivery outlives this method call and
        // never touches the caller's (about-to-be-disposed) scope. CancellationToken.None — the caller's token
        // is not ours to cancel the background work with once we've returned.
        _ = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IOutboundWebhookDispatcher scopedDispatcher =
                scope.ServiceProvider.GetRequiredService<IOutboundWebhookDispatcher>();
            Result<IReadOnlyList<OutboundEnqueueResult>> fanout =
                await scopedDispatcher.EnqueueForEventAsync(
                    broadcasterId,
                    committed.EventType,
                    variables,
                    committed.EventId,
                    CancellationToken.None
                );

            if (fanout.IsFailure)
                logger.LogError(
                    "Outbound webhook fanout failed for event {EventId} ({EventType}): {Error}",
                    committed.EventId,
                    committed.EventType,
                    fanout.ErrorMessage
                );
        });

        return Result.Success();
    }

    private static IReadOnlyDictionary<string, string> BuildVariables(EventRecord committed)
    {
        Dictionary<string, string> vars = new(StringComparer.OrdinalIgnoreCase)
        {
            ["event.id"] = committed.EventId.ToString(),
            ["event.type"] = committed.EventType,
            ["event.occurred_at"] = committed.OccurredAt.ToString("O"),
        };

        if (committed.BroadcasterId is { } bid)
            vars["event.broadcaster_id"] = bid.ToString();

        // Flatten top-level payload properties as event.{key} variables.
        try
        {
            JObject payload = JObject.Parse(committed.PayloadJson);
            foreach (JProperty prop in payload.Properties())
            {
                string key = $"event.{ToSnakeCase(prop.Name)}";
                string value = prop.Value.Type switch
                {
                    JTokenType.Null => "",
                    JTokenType.Object or JTokenType.Array => prop.Value.ToString(Formatting.None),
                    _ => prop.Value.ToString(),
                };
                vars[key] = value;
            }
        }
        catch (JsonException)
        {
            // Malformed payload — still deliver without the extra vars.
        }

        return vars;
    }

    // Converts PascalCase to snake_case for variable key generation.
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        System.Text.StringBuilder sb = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
