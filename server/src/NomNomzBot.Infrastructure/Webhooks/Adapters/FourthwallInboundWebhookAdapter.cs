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
/// Fourthwall inbound adapter (webhooks.md §3.3, supporter-events.md §0 D3). Fourthwall posts an
/// <c>application/json</c> body <c>{ "type": …, "webhookId": …, "data": { … } }</c> and signs it with
/// <c>X-Fourthwall-Hmac-SHA256 = base64(HMAC-SHA256(secret, rawBody))</c> — note **base64**, not the hex the
/// shared <see cref="IInboundSignatureVerifier"/> emits, so the MAC is computed inline here. The event kind is
/// the body <c>type</c> (e.g. <c>DONATION</c>) and the dedupe id is the body <c>webhookId</c>.
/// </summary>
public sealed class FourthwallInboundWebhookAdapter : IInboundWebhookAdapter
{
    public WebhookAdapterKind Kind => WebhookAdapterKind.Fourthwall;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(
            request.Headers,
            "X-Fourthwall-Hmac-SHA256"
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
                "Malformed Fourthwall payload.",
                "VALIDATION_FAILED"
            );

        string kind = variables.GetValueOrDefault("type", "unknown").ToLowerInvariant();
        // The webhook envelope id is the stable dedupe key; fall back to the resource id, then the clock.
        string eventId =
            variables.GetValueOrDefault("webhookId")
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
