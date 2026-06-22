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
/// GitHub inbound adapter (webhooks.md §3.3). Verifies <c>X-Hub-Signature-256 = "sha256=" + HMAC(secret, rawBody)</c>
/// in constant time; the event kind is the <c>X-GitHub-Event</c> header and the dedupe id is <c>X-GitHub-Delivery</c>.
/// </summary>
public sealed class GithubInboundWebhookAdapter(IInboundSignatureVerifier verifier)
    : IInboundWebhookAdapter
{
    public WebhookAdapterKind Kind => WebhookAdapterKind.Github;

    public Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    )
    {
        string? signature = WebhookAdapterHelpers.GetHeader(request.Headers, "X-Hub-Signature-256");
        if (string.IsNullOrEmpty(signature))
            return Result.Success(
                new WebhookVerification(false, WebhookRejectReason.InvalidSignature)
            );

        bool valid = verifier.Verify(secret, request.RawBody.Span, signature, "sha256=");
        return Result.Success(
            new WebhookVerification(valid, valid ? null : WebhookRejectReason.InvalidSignature)
        );
    }

    public Result<ParsedInboundEvent> Parse(
        InboundWebhookRequest request,
        GenericInboundConfig? genericConfig
    )
    {
        string kind =
            WebhookAdapterHelpers.GetHeader(request.Headers, "X-GitHub-Event") ?? "unknown";
        string eventId =
            WebhookAdapterHelpers.GetHeader(request.Headers, "X-GitHub-Delivery")
            ?? request.ReceivedAtUtc.Ticks.ToString();
        Dictionary<string, string> variables = WebhookAdapterHelpers.FlattenJson(
            Encoding.UTF8.GetString(request.RawBody.Span)
        );
        return Result.Success(new ParsedInboundEvent(kind, eventId, variables));
    }
}
