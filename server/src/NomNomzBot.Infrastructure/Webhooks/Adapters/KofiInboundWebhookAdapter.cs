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
/// Ko-fi inbound adapter (webhooks.md §3.3). Ko-fi posts <c>application/x-www-form-urlencoded</c> with a single
/// <c>data</c> field of JSON; there is no HMAC — verification is a constant-time compare of the JSON
/// <c>verification_token</c> against the stored secret (low-assurance; replay resistance is the body-hash dedup).
/// </summary>
public sealed class KofiInboundWebhookAdapter : IInboundWebhookAdapter
{
    public WebhookAdapterKind Kind => WebhookAdapterKind.Kofi;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? json = ExtractJson(request.RawBody.Span);
        if (json is null)
            return Result.Success(new WebhookVerification(false, WebhookRejectReason.Malformed));

        Dictionary<string, string> fields = WebhookAdapterHelpers.FlattenJson(json);
        if (!fields.TryGetValue("verification_token", out string? token))
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );

        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
        bool valid =
            tokenBytes.Length == secret.Length
            && CryptographicOperations.FixedTimeEquals(tokenBytes, secret);
        return Result.Success(
            new WebhookVerification(valid, valid ? null : WebhookRejectReason.InvalidSignature)
        );
    }

    public Result<ParsedInboundEvent> Parse(
        InboundWebhookRequest request,
        GenericInboundConfig? genericConfig
    )
    {
        string? json = ExtractJson(request.RawBody.Span);
        if (json is null)
            return Result.Failure<ParsedInboundEvent>(
                "Malformed Ko-fi payload.",
                "VALIDATION_FAILED"
            );

        Dictionary<string, string> variables = WebhookAdapterHelpers.FlattenJson(json);
        string kind = variables.GetValueOrDefault("type", "donation").ToLowerInvariant();
        string eventId = variables.GetValueOrDefault(
            "kofi_transaction_id",
            request.ReceivedAtUtc.Ticks.ToString()
        );
        return Result.Success(new ParsedInboundEvent(kind, eventId, variables));
    }

    private static string? ExtractJson(ReadOnlySpan<byte> body) =>
        WebhookAdapterHelpers.ParseForm(body).GetValueOrDefault("data");
}
