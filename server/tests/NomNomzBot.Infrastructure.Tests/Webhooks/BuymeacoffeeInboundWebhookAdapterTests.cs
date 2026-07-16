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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Webhooks;
using NomNomzBot.Infrastructure.Webhooks.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the Buy Me a Coffee inbound adapter (webhooks.md §3.3) verifies <c>X-Signature-Sha256</c> as a plain
/// <b>hex</b> HMAC-SHA256 over the raw body (no prefix — unlike GitHub's <c>sha256=</c>), and that Parse takes
/// the event kind from the envelope <c>type</c> and dedupes on the envelope <c>event_id</c>, which is stable
/// across redelivery attempts.
/// </summary>
public sealed class BuymeacoffeeInboundWebhookAdapterTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("bmc-webhook-secret");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"event_id":1234,"type":"donation.created","live_mode":true,"created":1719825600,"attempt":1,"data":{"id":91,"amount":5,"currency":"USD","supporter_name":"Alice","support_note":"keep it up!","note_hidden":false}}"""
    );

    private readonly BuymeacoffeeInboundWebhookAdapter _adapter = new(
        new InboundSignatureVerifier(
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero))
        )
    );

    private static InboundWebhookRequest Request(string? signature)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (signature is not null)
            headers["X-Signature-Sha256"] = signature;
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
        Convert.ToHexStringLower(HMACSHA256.HashData(Secret, Body));

    [Fact]
    public void Verify_CorrectHexSha256Hmac_IsValid()
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
    public void Verify_Base64EncodedDigest_IsRejected()
    {
        // BMC signs in hex, not base64 — accepting base64 would mean the encoding is not actually pinned.
        string base64 = Convert.ToBase64String(HMACSHA256.HashData(Secret, Body));

        Result<WebhookVerification> result = _adapter.Verify(Request(base64), Secret, null);

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
    public void Parse_TakesKindFromTypeAndDedupesOnEventId()
    {
        Result<ParsedInboundEvent> result = _adapter.Parse(Request(ValidSignature()), null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be("donation.created");
        result.Value.ProviderEventId.Should().Be("1234"); // envelope event_id, stable across attempts
        result.Value.Variables["data.amount"].Should().Be("5");
        result.Value.Variables["data.supporter_name"].Should().Be("Alice");
    }
}
