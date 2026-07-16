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
/// Proves the Fourthwall inbound adapter (webhooks.md §3.3) verifies its <c>X-Fourthwall-Hmac-SHA256</c> header
/// as a <b>base64</b> HMAC-SHA256 over the raw body — the key detail that distinguishes it from the hex the
/// shared verifier emits — accepting a correct signature, rejecting a tampered or absent one, and parsing the
/// body <c>type</c>/<c>webhookId</c> into the normalized kind + dedupe id.
/// </summary>
public sealed class FourthwallInboundWebhookAdapterTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("fw-webhook-secret");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"type":"DONATION","webhookId":"wh_9","data":{"id":"don_1","amounts":{"total":{"value":10,"currency":"USD"}}}}"""
    );

    private readonly FourthwallInboundWebhookAdapter _adapter = new();

    private static InboundWebhookRequest Request(string? signature)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (signature is not null)
            headers["X-Fourthwall-Hmac-SHA256"] = signature;
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

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.Reason.Should().BeNull();
    }

    [Fact]
    public void Verify_HexEncodedHmac_IsRejected()
    {
        // A hex digest of the right bytes must still fail — Fourthwall signs in base64, and accepting hex would
        // mean the constant-time compare is not actually pinning the encoding.
        string hex = Convert.ToHexStringLower(HMACSHA256.HashData(Secret, Body));

        Result<WebhookVerification> result = _adapter.Verify(Request(hex), Secret, null);

        result.Value.IsValid.Should().BeFalse();
        result.Value.Reason.Should().Be(WebhookRejectReason.InvalidSignature);
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
    public void Parse_ReadsKindFromTypeAndDedupeIdFromWebhookId()
    {
        Result<ParsedInboundEvent> result = _adapter.Parse(Request(ValidSignature()), null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("donation");
        result.Value.ProviderEventId.Should().Be("wh_9");
        result.Value.Variables["data.amounts.total.value"].Should().Be("10");
    }
}
