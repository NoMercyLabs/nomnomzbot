// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Outbound webhook delivery (webhooks.md §3.6). Renders the body, Standard-Webhooks-signs with BOTH the current
/// and rotation secrets, and POSTs through the SSRF-hardened <c>egress-allowlisted</c> client (resolve-then-pin,
/// https-only, no redirects). A 2xx delivers and resets the failure counter; otherwise the attempt is scheduled
/// for retry (exponential backoff) and the endpoint auto-disables after the failure threshold.
/// (Deferred — documented: the spec's IIdempotencyGuard does not exist; the per-send webhook-id is the dedupe.)
/// </summary>
public sealed class OutboundWebhookDispatcher(
    IApplicationDbContext db,
    ITokenProtector tokenProtector,
    IOutboundWebhookSigner signer,
    ITemplateEngine templateEngine,
    IHttpClientFactory httpClientFactory,
    IEventBus eventBus,
    TimeProvider clock
) : IOutboundWebhookDispatcher
{
    private const int AutoDisableThreshold = 20;

    public async Task<Result<IReadOnlyList<OutboundEnqueueResult>>> EnqueueForEventAsync(
        Guid broadcasterId,
        string eventType,
        IReadOnlyDictionary<string, string> variables,
        Guid? journalEventId,
        CancellationToken ct = default
    )
    {
        List<OutboundWebhookEndpoint> enabled = await db
            .OutboundWebhookEndpoints.Where(e =>
                e.BroadcasterId == broadcasterId && e.IsEnabled && e.DeletedAt == null
            )
            .ToListAsync(ct);

        List<OutboundEnqueueResult> results = [];
        foreach (OutboundWebhookEndpoint endpoint in enabled.Where(e => Subscribes(e, eventType)))
            results.Add(await EnqueueOneAsync(endpoint, eventType, variables, journalEventId, ct));
        return Result.Success<IReadOnlyList<OutboundEnqueueResult>>(results);
    }

    public async Task<Result<OutboundEnqueueResult>> EnqueueForEndpointAsync(
        Guid broadcasterId,
        Guid endpointId,
        string eventType,
        IReadOnlyDictionary<string, string> variables,
        Guid? journalEventId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await db.OutboundWebhookEndpoints.FirstOrDefaultAsync(
            e => e.Id == endpointId && e.BroadcasterId == broadcasterId && e.DeletedAt == null,
            ct
        );
        if (endpoint is null)
            return Result.Failure<OutboundEnqueueResult>("Endpoint not found.", "NOT_FOUND");
        return Result.Success(
            await EnqueueOneAsync(endpoint, eventType, variables, journalEventId, ct)
        );
    }

    public async Task<Result<WebhookDeliveryStatus>> AttemptDeliveryAsync(
        OutboundWebhookDelivery delivery,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await db.OutboundWebhookEndpoints.FirstOrDefaultAsync(
            e => e.Id == delivery.EndpointId,
            ct
        );
        if (endpoint is null)
            return Result.Failure<WebhookDeliveryStatus>("Endpoint not found.", "NOT_FOUND");
        WebhookDeliveryStatus status = await AttemptCoreAsync(endpoint, delivery, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success(status);
    }

    private async Task<OutboundEnqueueResult> EnqueueOneAsync(
        OutboundWebhookEndpoint endpoint,
        string eventType,
        IReadOnlyDictionary<string, string> variables,
        Guid? journalEventId,
        CancellationToken ct
    )
    {
        Guid webhookMessageId = Guid.CreateVersion7();
        string body = endpoint.BodyTemplate is null
            ? JsonConvert.SerializeObject(variables)
            : templateEngine.Render(endpoint.BodyTemplate, variables);

        OutboundWebhookDelivery delivery = new()
        {
            BroadcasterId = endpoint.BroadcasterId,
            EndpointId = endpoint.Id,
            WebhookMessageId = webhookMessageId,
            JournalEventId = journalEventId,
            EventType = eventType,
            RenderedBody = body,
            Attempt = 1,
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.OutboundWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new OutboundWebhookEnqueuedEvent
            {
                BroadcasterId = endpoint.BroadcasterId,
                OutboundEndpointId = endpoint.Id,
                WebhookMessageId = webhookMessageId,
                EventType = eventType,
                JournalEventId = journalEventId,
            },
            ct
        );

        WebhookDeliveryStatus status = await AttemptCoreAsync(endpoint, delivery, ct);
        await db.SaveChangesAsync(ct);
        return new OutboundEnqueueResult(endpoint.Id, webhookMessageId, delivery.Id, status);
    }

    private async Task<WebhookDeliveryStatus> AttemptCoreAsync(
        OutboundWebhookEndpoint endpoint,
        OutboundWebhookDelivery delivery,
        CancellationToken ct
    )
    {
        List<byte[]> secrets = await UnwrapSecretsAsync(endpoint, ct);
        byte[] body = Encoding.UTF8.GetBytes(delivery.RenderedBody);

        int? responseCode = null;
        string? error = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (secrets.Count == 0)
        {
            error = "Signing secret could not be decrypted.";
        }
        else
        {
            try
            {
                HttpResponseMessage response = await SendAsync(
                    endpoint,
                    delivery,
                    body,
                    secrets,
                    ct
                );
                responseCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    error = $"Non-success status {responseCode}.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                error = ex.Message; // transport error — scrubbed to the message only
            }
        }
        stopwatch.Stop();

        bool delivered = error is null;
        delivery.ResponseCode = responseCode;
        delivery.DurationMs = (int)stopwatch.ElapsedMilliseconds;
        delivery.Error = error;
        return await ApplyOutcomeAsync(endpoint, delivery, delivered, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        OutboundWebhookEndpoint endpoint,
        OutboundWebhookDelivery delivery,
        byte[] body,
        IReadOnlyList<byte[]> secrets,
        CancellationToken ct
    )
    {
        WebhookSignatureHeaders signature = signer.Sign(
            delivery.WebhookMessageId.ToString(),
            clock.GetUtcNow().ToUnixTimeSeconds(),
            body,
            secrets
        );

        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://{endpoint.Fqdn}{endpoint.Path ?? string.Empty}"
        )
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("webhook-id", signature.WebhookId);
        request.Headers.TryAddWithoutValidation("webhook-timestamp", signature.Timestamp);
        request.Headers.TryAddWithoutValidation("webhook-signature", signature.Signature);
        if (endpoint.CustomHeadersJson is not null)
            foreach (
                KeyValuePair<string, string> header in JsonConvert.DeserializeObject<
                    Dictionary<string, string>
                >(endpoint.CustomHeadersJson) ?? []
            )
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return await httpClientFactory.CreateClient(EgressHttpClient.Name).SendAsync(request, ct);
    }

    private async Task<WebhookDeliveryStatus> ApplyOutcomeAsync(
        OutboundWebhookEndpoint endpoint,
        OutboundWebhookDelivery delivery,
        bool delivered,
        CancellationToken ct
    )
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        endpoint.LastDeliveryAt = now;

        if (delivered)
        {
            endpoint.ConsecutiveFailureCount = 0;
            endpoint.LastSuccessAt = now;
            delivery.Status = WebhookDeliveryStatus.Delivered;
            delivery.NextRetryAt = null;
        }
        else
        {
            endpoint.ConsecutiveFailureCount++;
            if (endpoint.ConsecutiveFailureCount >= AutoDisableThreshold)
            {
                endpoint.IsEnabled = false;
                endpoint.DisabledAt = now;
                endpoint.DisabledReason = "Too many consecutive delivery failures.";
                delivery.Status = WebhookDeliveryStatus.DeadLetter;
                delivery.NextRetryAt = null;
                await eventBus.PublishAsync(
                    new OutboundWebhookAutoDisabledEvent
                    {
                        BroadcasterId = endpoint.BroadcasterId,
                        OutboundEndpointId = endpoint.Id,
                        ConsecutiveFailureCount = endpoint.ConsecutiveFailureCount,
                        Reason = "consecutive_failures",
                    },
                    ct
                );
            }
            else
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.NextRetryAt = now.AddSeconds(30 * Math.Pow(2, delivery.Attempt - 1));
            }
        }

        await eventBus.PublishAsync(
            new OutboundWebhookAttemptedEvent
            {
                BroadcasterId = endpoint.BroadcasterId,
                OutboundEndpointId = endpoint.Id,
                WebhookMessageId = delivery.WebhookMessageId,
                Attempt = delivery.Attempt,
                Status = delivery.Status,
                ResponseCode = delivery.ResponseCode,
                NextRetryAt = delivery.NextRetryAt,
            },
            ct
        );
        return delivery.Status;
    }

    private async Task<List<byte[]>> UnwrapSecretsAsync(
        OutboundWebhookEndpoint endpoint,
        CancellationToken ct
    )
    {
        TokenProtectionContext context = new(
            endpoint.BroadcasterId.ToString(),
            "webhook:out",
            endpoint.Id.ToString()
        );
        List<byte[]> secrets = [];
        string? primary = await tokenProtector.TryUnprotectAsync(
            endpoint.SigningSecretEnvelope,
            context,
            ct
        );
        if (primary is not null)
            secrets.Add(Encoding.UTF8.GetBytes(primary));
        if (endpoint.SecondarySigningSecretEnvelope is not null)
        {
            string? secondary = await tokenProtector.TryUnprotectAsync(
                endpoint.SecondarySigningSecretEnvelope,
                context,
                ct
            );
            if (secondary is not null)
                secrets.Add(Encoding.UTF8.GetBytes(secondary));
        }
        return secrets;
    }

    private static bool Subscribes(OutboundWebhookEndpoint endpoint, string eventType)
    {
        List<string> subscribed =
            JsonConvert.DeserializeObject<List<string>>(endpoint.SubscribedEventTypesJson) ?? [];
        return subscribed.Contains("*") || subscribed.Contains(eventType);
    }
}
