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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks.Adapters;

/// <summary>
/// Shopify inbound adapter (webhooks.md §3.3, supporter-events.md §0 D3). Shopify posts an
/// <c>application/json</c> order body and signs it with <c>X-Shopify-Hmac-SHA256 = base64(HMAC-SHA256(secret,
/// rawBody))</c> — base64, like Fourthwall, not the hex the shared verifier emits, so the MAC is computed inline.
/// The event kind is the <c>X-Shopify-Topic</c> header (e.g. <c>orders/create</c>, normalized to
/// <c>orders.create</c>) and the dedupe id is <c>X-Shopify-Webhook-Id</c> (falling back to <c>X-Shopify-Event-Id</c>).
/// </summary>
public sealed class ShopifyInboundWebhookAdapter : IInboundWebhookAdapter
{
    public WebhookAdapterKind Kind => WebhookAdapterKind.Shopify;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(
            request.Headers,
            "X-Shopify-Hmac-SHA256"
        );
        if (string.IsNullOrEmpty(signature))
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(secret, request.RawBody.Span, mac);
        string computed = Convert.ToBase64String(mac);

        bool valid = FixedTimeStringEquals(signature, computed);
        return Result.Success(
            new WebhookVerification(valid, valid ? null : WebhookRejectReason.InvalidSignature)
        );
    }

    public Result<ParsedInboundEvent> Parse(
        InboundWebhookRequest request,
        GenericInboundConfig? genericConfig
    )
    {
        string json = Encoding.UTF8.GetString(request.RawBody.Span);
        Dictionary<string, string> variables = WebhookAdapterHelpers.FlattenJson(json);
        if (variables.Count == 0)
            return Result.Failure<ParsedInboundEvent>(
                "Malformed Shopify payload.",
                "VALIDATION_FAILED"
            );

        string kind = (
            WebhookAdapterHelpers.GetHeader(request.Headers, "X-Shopify-Topic") ?? "unknown"
        )
            .Replace('/', '.')
            .ToLowerInvariant();
        string eventId =
            WebhookAdapterHelpers.GetHeader(request.Headers, "X-Shopify-Webhook-Id")
            ?? WebhookAdapterHelpers.GetHeader(request.Headers, "X-Shopify-Event-Id")
            ?? variables.GetValueOrDefault("id")
            ?? request.ReceivedAtUtc.Ticks.ToString();
        return Result.Success(new ParsedInboundEvent(kind, eventId, variables));
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length
            && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
