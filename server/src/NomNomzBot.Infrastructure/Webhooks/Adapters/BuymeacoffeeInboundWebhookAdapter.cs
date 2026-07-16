// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks.Adapters;

/// <summary>
/// Buy Me a Coffee inbound adapter (webhooks.md §3.3, supporter-events.md §0 D3). BMC signs the raw JSON body
/// with <c>X-Signature-Sha256 = hex(HMAC-SHA256(secret, rawBody))</c> — plain hex, no prefix — which the shared
/// <see cref="IInboundSignatureVerifier"/> checks in constant time. Every event shares one envelope
/// <c>{ event_id, type, live_mode, created, attempt, data }</c>: the event kind is the body <c>type</c>
/// (e.g. <c>donation.created</c>) and the dedupe id is <c>event_id</c>, which is stable across redelivery
/// attempts (the attempt counter is a separate field).
/// </summary>
public sealed class BuymeacoffeeInboundWebhookAdapter(IInboundSignatureVerifier verifier)
    : IInboundWebhookAdapter
{
    public WebhookAdapterKind Kind => WebhookAdapterKind.Buymeacoffee;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(request.Headers, "X-Signature-Sha256");
        if (string.IsNullOrEmpty(signature))
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );

        bool valid = verifier.Verify(secret, request.RawBody.Span, signature, prefix: "");
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
                "Malformed Buy Me a Coffee payload.",
                "VALIDATION_FAILED"
            );

        string kind = variables.GetValueOrDefault("type", "unknown").ToLowerInvariant();
        string eventId =
            variables.GetValueOrDefault("event_id") ?? request.ReceivedAtUtc.Ticks.ToString();
        return Result.Success(new ParsedInboundEvent(kind, eventId, variables));
    }
}
