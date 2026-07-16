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
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Webhooks.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the Shopify inbound adapter (webhooks.md §3.3) verifies <c>X-Shopify-Hmac-SHA256</c> as a
/// <b>base64</b> HMAC-SHA256 over the raw body, reads the event kind from the <c>X-Shopify-Topic</c> header
/// (normalizing <c>orders/create</c> → <c>orders.create</c>), and takes the dedupe id from
/// <c>X-Shopify-Webhook-Id</c>.
/// </summary>
public sealed class ShopifyInboundWebhookAdapterTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("shopify-app-secret");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"id":123,"total_price":"10.00","currency":"USD","line_items":[{"title":"Tee","quantity":1}]}"""
    );

    private readonly ShopifyInboundWebhookAdapter _adapter = new();

    private static InboundWebhookRequest Request(string? signature, bool withTopic = true)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (signature is not null)
            headers["X-Shopify-Hmac-SHA256"] = signature;
        if (withTopic)
        {
            headers["X-Shopify-Topic"] = "orders/create";
            headers["X-Shopify-Webhook-Id"] = "wh-42";
        }
        return new InboundWebhookRequest
        {
            Token = "tok",
            Method = "POST",
            ContentType = "application/json",
            Headers = headers,
            RawBody = Body,
            ReceivedAtUtc = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            RemoteIpHash = "iphash",
        };
    }

    private static string ValidSignature() =>
        Convert.ToBase64String(HMACSHA256.HashData(Secret, Body));

    [Fact]
    public void Verify_CorrectBase64Hmac_IsValid()
    {
        Result<WebhookVerification> result = _adapter.Verify(
            Request(ValidSignature()),
            Secret,
            null
        );

        result.Value.IsValid.Should().BeTrue();
        result.Value.Reason.Should().BeNull();
    }

    [Fact]
    public void Verify_TamperedSignature_IsRejected()
    {
        Result<WebhookVerification> result = _adapter.Verify(
            Request("bm90LXRoZS1zaWc="),
            Secret,
            null
        );

        result.Value.IsValid.Should().BeFalse();
        result.Value.Reason.Should().Be(WebhookRejectReason.InvalidSignature);
    }

    [Fact]
    public void Verify_MissingSignatureHeader_IsRejected()
    {
        Result<WebhookVerification> result = _adapter.Verify(Request(null), Secret, null);

        result.Value.IsValid.Should().BeFalse();
        result.Value.Reason.Should().Be(WebhookRejectReason.InvalidSignature);
    }

    [Fact]
    public void Parse_NormalizesTopicToKindAndTakesWebhookIdAsDedupeId()
    {
        Result<ParsedInboundEvent> result = _adapter.Parse(Request(ValidSignature()), null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("orders.create"); // slash normalized
        result.Value.ProviderEventId.Should().Be("wh-42");
        result.Value.Variables["total_price"].Should().Be("10.00");
    }
}
