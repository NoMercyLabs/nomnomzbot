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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks.Adapters;

/// <summary>
/// Generic / Standard-Webhooks inbound adapter (webhooks.md §3.3). Two modes per <see cref="GenericInboundConfig"/>:
/// HMAC (a signature header over a templated signing string, with a REQUIRED timestamp header → 10-min replay
/// window) or shared-secret-in-body (a JSON field that must equal the secret — low-assurance, like Ko-fi). The
/// event kind + dedupe id come from configured JSONPaths.
/// </summary>
public sealed class GenericInboundWebhookAdapter(IInboundSignatureVerifier verifier)
    : IInboundWebhookAdapter
{
    private static readonly TimeSpan ReplayTolerance = TimeSpan.FromMinutes(10);

    public WebhookAdapterKind Kind => WebhookAdapterKind.Generic;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        if (genericConfig is null)
            return Result.Success(new WebhookVerification(false, WebhookRejectReason.Malformed));

        return genericConfig.SignatureHeaderName is null
            ? VerifySharedSecret(request, secret, genericConfig)
            : VerifyHmac(request, secret, genericConfig);
    }

    public Result<ParsedInboundEvent> Parse(
        InboundWebhookRequest request,
        GenericInboundConfig? genericConfig
    )
    {
        if (genericConfig is null)
            return Result.Failure<ParsedInboundEvent>(
                "Missing generic config.",
                "VALIDATION_FAILED"
            );

        string json = Encoding.UTF8.GetString(request.RawBody.Span);
        JToken root;
        try
        {
            root = JToken.Parse(json);
        }
        catch (JsonException)
        {
            return Result.Failure<ParsedInboundEvent>("Malformed JSON body.", "VALIDATION_FAILED");
        }

        string kind = root.SelectToken(genericConfig.EventKindJsonPath)?.ToString() ?? "event";
        string eventId =
            root.SelectToken(genericConfig.ProviderEventIdJsonPath)?.ToString()
            ?? request.ReceivedAtUtc.Ticks.ToString();
        return Result.Success(
            new ParsedInboundEvent(kind, eventId, WebhookAdapterHelpers.FlattenJson(json))
        );
    }

    private static Result<WebhookVerification> VerifySharedSecret(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig config
    )
    {
        if (config.SharedSecretBodyField is null)
            return Result.Success(new WebhookVerification(false, WebhookRejectReason.Malformed));

        Dictionary<string, string> body = WebhookAdapterHelpers.FlattenJson(
            Encoding.UTF8.GetString(request.RawBody.Span)
        );
        byte[] value = Encoding.UTF8.GetBytes(
            body.GetValueOrDefault(config.SharedSecretBodyField, string.Empty)
        );
        bool valid =
            value.Length == secret.Length && CryptographicOperations.FixedTimeEquals(value, secret);
        return Result.Success(
            new WebhookVerification(valid, valid ? null : WebhookRejectReason.InvalidSignature)
        );
    }

    private Result<WebhookVerification> VerifyHmac(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig config
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(
            request.Headers,
            config.SignatureHeaderName!
        );
        if (signature is null)
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );
        if (config.TimestampHeaderName is null) // config invariant — HMAC mode requires a timestamp header
            return Result.Success(new WebhookVerification(false, WebhookRejectReason.Malformed));

        string? timestamp = WebhookAdapterHelpers.GetHeader(
            request.Headers,
            config.TimestampHeaderName
        );
        if (timestamp is null || !long.TryParse(timestamp, out long timestampUnix))
            return Result.Success(new WebhookVerification(false, WebhookRejectReason.Malformed));

        string template = config.SigningStringTemplate ?? "{id}.{timestamp}.{body}";
        string signingString = template
            .Replace(
                "{id}",
                WebhookAdapterHelpers.GetHeader(request.Headers, "webhook-id") ?? string.Empty
            )
            .Replace("{timestamp}", timestampUnix.ToString())
            .Replace("{body}", Encoding.UTF8.GetString(request.RawBody.Span));

        // VerifyWithTimestamp enforces the replay window with its own injected clock; a stale OR forged message
        // both fail here. We report InvalidSignature (the adapter stays pure — no clock of its own).
        bool valid = verifier.VerifyWithTimestamp(
            secret,
            Encoding.UTF8.GetBytes(signingString),
            signature,
            config.SignaturePrefix ?? string.Empty,
            timestampUnix,
            ReplayTolerance
        );
        return Result.Success(
            new WebhookVerification(valid, valid ? null : WebhookRejectReason.InvalidSignature)
        );
    }
}
