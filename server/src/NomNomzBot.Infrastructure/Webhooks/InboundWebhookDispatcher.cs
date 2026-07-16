// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// The core inbound ingest path (webhooks.md §3.2): resolve → verify → parse → dedup → journal → fan-out. Dedup
/// rides the journal's idempotent-on-EventId behaviour via an endpoint-salted UUIDv5 (§3.2.1) so provider ids can
/// never collide across tenants. Fan-out is a single <c>InboundWebhookReceivedEvent</c> a downstream consumer
/// routes to the target pipeline / event-response (keeping execution out of the ingest hot path). An unknown token
/// returns 404 with NO bus event (no free amplification for an anonymous flooder).
/// </summary>
public sealed class InboundWebhookDispatcher(
    IApplicationDbContext db,
    ITokenProtector tokenProtector,
    IEnumerable<IInboundWebhookAdapter> adapters,
    IEventJournal journal,
    IEventBus eventBus,
    TimeProvider clock
) : IInboundWebhookDispatcher
{
    // Fixed namespace GUID for the deterministic inbound EventId (webhooks.md §3.2.1, RFC 9562 UUIDv5).
    private static readonly Guid WebhookNamespace = new("d6b4f0a2-9c7e-5b1d-8e3a-2f6c4b9a7e10");

    public async Task<Result<InboundDispatchResult>> DispatchAsync(
        InboundWebhookRequest request,
        CancellationToken ct = default
    )
    {
        InboundWebhookEndpoint? endpoint = await db.InboundWebhookEndpoints.FirstOrDefaultAsync(
            e => e.Token == request.Token && e.DeletedAt == null,
            ct
        );
        if (endpoint is null)
            // No bus event on the unknown-token path — it must not amplify into the event bus.
            return Result.Success(Reject(null, WebhookRejectReason.UnknownEndpoint, 404));
        if (!endpoint.IsEnabled)
            return await EmitRejectAsync(endpoint, WebhookRejectReason.Disabled, 503, ct);

        IInboundWebhookAdapter? adapter = adapters.FirstOrDefault(a =>
            a.Kind == endpoint.AdapterKind
        );
        if (adapter is null)
            return await EmitRejectAsync(endpoint, WebhookRejectReason.Malformed, 400, ct);

        string? secret = await tokenProtector.TryUnprotectAsync(
            endpoint.VerificationSecretEnvelope,
            new TokenProtectionContext(
                endpoint.BroadcasterId.ToString(),
                "webhook:in",
                endpoint.Id.ToString()
            ),
            ct
        );
        if (secret is null)
            return await EmitRejectAsync(endpoint, WebhookRejectReason.InvalidSignature, 403, ct);

        GenericInboundConfig? genericConfig = endpoint.GenericConfigJson is null
            ? null
            : JsonConvert.DeserializeObject<GenericInboundConfig>(endpoint.GenericConfigJson);

        WebhookVerification verification = adapter
            .Verify(request, Encoding.UTF8.GetBytes(secret), genericConfig)
            .Value;
        if (!verification.IsValid)
        {
            WebhookRejectReason reason =
                verification.Reason ?? WebhookRejectReason.InvalidSignature;
            return await EmitRejectAsync(endpoint, reason, StatusFor(reason), ct);
        }

        Result<ParsedInboundEvent> parsed = adapter.Parse(request, genericConfig);
        if (parsed.IsFailure)
            return await EmitRejectAsync(endpoint, WebhookRejectReason.Malformed, 400, ct);

        string eventType = $"webhook.{ProviderLabel(endpoint.AdapterKind)}.{parsed.Value.Kind}";
        Guid eventId = WebhookEventId(
            endpoint.BroadcasterId,
            endpoint.Id,
            DedupKey(
                endpoint.AdapterKind,
                genericConfig,
                parsed.Value.ProviderEventId,
                request.RawBody.Span
            )
        );

        // Dedup via the journal's idempotent-on-EventId guarantee (GetByEventId succeeds iff already journaled).
        if ((await journal.GetByEventIdAsync(eventId, ct)).IsSuccess)
            return Result.Success(
                new InboundDispatchResult(
                    true,
                    true,
                    eventId,
                    0,
                    eventType,
                    null,
                    200,
                    endpoint.Id,
                    endpoint.BroadcasterId,
                    endpoint.AdapterKind
                )
            );

        Result<EventRecord> appended = await journal.AppendAsync(
            new AppendEventRequest(
                eventId,
                endpoint.BroadcasterId,
                eventType,
                1,
                "webhook",
                JsonConvert.SerializeObject(parsed.Value.Variables),
                "{}",
                clock.GetUtcNow().UtcDateTime
            ),
            ct
        );
        if (appended.IsFailure)
            return Result.Failure<InboundDispatchResult>(appended.ErrorMessage, appended.ErrorCode);

        endpoint.LastReceivedAt = clock.GetUtcNow().UtcDateTime;
        endpoint.ReceiveCount++;
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new InboundWebhookReceivedEvent
            {
                BroadcasterId = endpoint.BroadcasterId,
                InboundEndpointId = endpoint.Id,
                Adapter = endpoint.AdapterKind,
                EventType = eventType,
                JournalEventId = appended.Value.EventId,
                StreamPosition = appended.Value.StreamPosition,
                ProviderEventId = parsed.Value.ProviderEventId,
                WasDuplicate = false,
            },
            ct
        );

        return Result.Success(
            new InboundDispatchResult(
                true,
                false,
                appended.Value.EventId,
                appended.Value.StreamPosition,
                eventType,
                null,
                200,
                endpoint.Id,
                endpoint.BroadcasterId,
                endpoint.AdapterKind
            )
        );
    }

    private async Task<Result<InboundDispatchResult>> EmitRejectAsync(
        InboundWebhookEndpoint endpoint,
        WebhookRejectReason reason,
        int httpStatus,
        CancellationToken ct
    )
    {
        await eventBus.PublishAsync(
            new InboundWebhookRejectedEvent
            {
                BroadcasterId = endpoint.BroadcasterId,
                InboundEndpointId = endpoint.Id,
                Adapter = endpoint.AdapterKind,
                Reason = reason,
                HttpStatus = httpStatus,
            },
            ct
        );
        return Result.Success(Reject(endpoint, reason, httpStatus));
    }

    private static InboundDispatchResult Reject(
        InboundWebhookEndpoint? endpoint,
        WebhookRejectReason reason,
        int httpStatus
    ) =>
        new(
            false,
            false,
            null,
            0,
            string.Empty,
            reason,
            httpStatus,
            endpoint?.Id,
            endpoint?.BroadcasterId,
            endpoint?.AdapterKind
        );

    /// <summary>
    /// The dedup identity. HMAC adapters bind the body to the secret, so the provider id alone is safe; no-HMAC
    /// adapters fold the body hash in so a forged request cannot pre-claim (shadow) a genuine future event's id.
    /// </summary>
    private static string DedupKey(
        WebhookAdapterKind kind,
        GenericInboundConfig? config,
        string providerEventId,
        ReadOnlySpan<byte> rawBody
    )
    {
        bool hmacBound =
            kind == WebhookAdapterKind.Github
            || (kind == WebhookAdapterKind.Generic && config?.SignatureHeaderName is not null);
        return hmacBound
            ? providerEventId
            : $"{Convert.ToHexStringLower(SHA256.HashData(rawBody))}:{providerEventId}";
    }

    private static Guid WebhookEventId(Guid broadcasterId, Guid endpointId, string dedupKey) =>
        Uuid5(WebhookNamespace, $"{broadcasterId:N}|{endpointId:N}|{dedupKey}");

    /// <summary>RFC 9562 §5.5 UUIDv5 (SHA-1, big-endian) — the framework ships v7 but not v5.</summary>
    private static Guid Uuid5(Guid namespaceId, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceId.TryWriteBytes(namespaceBytes, bigEndian: true, out _);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        byte[] toHash = new byte[16 + nameBytes.Length];
        namespaceBytes.CopyTo(toHash);
        nameBytes.CopyTo(toHash, 16);
        byte[] hash = SHA1.HashData(toHash);

        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(guidBytes, bigEndian: true);
    }

    private static string ProviderLabel(WebhookAdapterKind kind) =>
        kind switch
        {
            WebhookAdapterKind.Kofi => "kofi",
            WebhookAdapterKind.Github => "github",
            WebhookAdapterKind.Fourthwall => "fourthwall",
            WebhookAdapterKind.Shopify => "shopify",
            WebhookAdapterKind.Patreon => "patreon",
            _ => "generic",
        };

    private static int StatusFor(WebhookRejectReason reason) =>
        reason switch
        {
            WebhookRejectReason.ReplayWindow => 403,
            WebhookRejectReason.Malformed => 400,
            _ => 401,
        };
}
