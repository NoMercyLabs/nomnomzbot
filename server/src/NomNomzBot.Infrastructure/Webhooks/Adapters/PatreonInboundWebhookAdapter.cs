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
/// Patreon inbound adapter (webhooks.md §3.3, supporter-events.md §0 D3). Patreon signs the raw JSON:API body
/// with <c>X-Patreon-Signature = hex(HMAC-<b>MD5</b>(secret, rawBody))</c> — a different algorithm AND encoding
/// from the base64-SHA256 providers, so the MAC is computed inline. The event kind lives in the
/// <c>X-Patreon-Event</c> header (e.g. <c>members:pledge:create</c>); since the body cannot distinguish a new
/// pledge from a cancellation, the raw event is <b>injected</b> into the flattened bag under
/// <c>patreon.event</c> so the supporter source can gate on it. There is no native event id, so the dedupe id
/// is a composite of the member id + last-charge date.
/// </summary>
public sealed class PatreonInboundWebhookAdapter : IInboundWebhookAdapter
{
    private const string EventKey = "patreon.event";

    public WebhookAdapterKind Kind => WebhookAdapterKind.Patreon;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(request.Headers, "X-Patreon-Signature");
        if (string.IsNullOrEmpty(signature))
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );

        Span<byte> mac = stackalloc byte[16]; // MD5 digest length
        HMACMD5.HashData(secret, request.RawBody.Span, mac);
        string computed = Convert.ToHexStringLower(mac);

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
                "Malformed Patreon payload.",
                "VALIDATION_FAILED"
            );

        string rawEvent = WebhookAdapterHelpers.GetHeader(request.Headers, "X-Patreon-Event") ?? "";
        // The trigger is header-only; carry it into the journaled bag so the supporter source can gate a new
        // pledge apart from an update/cancellation (the body looks identical across the three).
        variables[EventKey] = rawEvent;

        string kind =
            rawEvent.Length == 0 ? "unknown" : rawEvent.Replace(':', '.').ToLowerInvariant();
        // No native per-event id — dedupe on the member + its last charge so redeliveries collapse but a new
        // monthly charge is a fresh event.
        string eventId = Compose(variables) ?? request.ReceivedAtUtc.Ticks.ToString();
        return Result.Success(new ParsedInboundEvent(kind, eventId, variables));
    }

    private static string? Compose(Dictionary<string, string> variables)
    {
        string? memberId = variables.GetValueOrDefault("data.id");
        if (string.IsNullOrEmpty(memberId))
            return null;
        string charge = variables.GetValueOrDefault("data.attributes.last_charge_date", "none");
        return $"{memberId}:{charge}";
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length
            && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
