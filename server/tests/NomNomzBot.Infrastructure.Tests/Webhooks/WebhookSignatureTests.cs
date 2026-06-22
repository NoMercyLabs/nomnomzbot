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
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Infrastructure.Webhooks;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the webhook HMAC primitives (webhooks.md §3.4/§3.8): inbound verification accepts a correct signature,
/// rejects a forgery, and rejects a stale timestamp (replay); outbound Standard Webhooks signing produces the
/// exact <c>v1,&lt;base64&gt;</c> over <c>id.timestamp.payload</c> and one signature per active secret (rotation).
/// </summary>
public sealed class WebhookSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inbound_verify_accepts_a_correct_signature_and_rejects_a_forgery()
    {
        InboundSignatureVerifier sut = new(new FakeTimeProvider(Now));
        byte[] secret = Encoding.UTF8.GetBytes("whsec");
        byte[] signingString = Encoding.UTF8.GetBytes("the-signed-body");
        string good =
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, signingString));

        sut.Verify(secret, signingString, good, "sha256=").Should().BeTrue();
        sut.Verify(secret, signingString, "sha256=deadbeef", "sha256=").Should().BeFalse();
        sut.Verify(secret, Encoding.UTF8.GetBytes("tampered"), good, "sha256=").Should().BeFalse();
    }

    [Fact]
    public void Inbound_verify_with_timestamp_rejects_a_stale_message()
    {
        InboundSignatureVerifier sut = new(new FakeTimeProvider(Now));
        byte[] secret = Encoding.UTF8.GetBytes("whsec");
        byte[] signingString = Encoding.UTF8.GetBytes("body");
        string good = Convert.ToHexStringLower(HMACSHA256.HashData(secret, signingString));
        long now = Now.ToUnixTimeSeconds();

        sut.VerifyWithTimestamp(secret, signingString, good, "", now, TimeSpan.FromMinutes(10))
            .Should()
            .BeTrue();
        sut.VerifyWithTimestamp(
                secret,
                signingString,
                good,
                "",
                now - 3600, // an hour old
                TimeSpan.FromMinutes(10)
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Outbound_sign_produces_the_standard_webhooks_signature()
    {
        OutboundWebhookSigner sut = new();
        string id = "msg_123";
        long ts = Now.ToUnixTimeSeconds();
        byte[] payload = Encoding.UTF8.GetBytes("{\"a\":1}");
        byte[] secret = Encoding.UTF8.GetBytes("whsec_primary");

        WebhookSignatureHeaders headers = sut.Sign(id, ts, payload, [secret]);

        headers.WebhookId.Should().Be(id);
        headers.Timestamp.Should().Be(ts.ToString());
        byte[] signedContent = Encoding.UTF8.GetBytes($"{id}.{ts}.{{\"a\":1}}");
        string expected =
            "v1," + Convert.ToBase64String(HMACSHA256.HashData(secret, signedContent));
        headers.Signature.Should().Be(expected);
    }

    [Fact]
    public void Outbound_sign_emits_one_signature_per_active_secret_for_rotation()
    {
        OutboundWebhookSigner sut = new();
        byte[] primary = Encoding.UTF8.GetBytes("primary");
        byte[] secondary = Encoding.UTF8.GetBytes("secondary");

        WebhookSignatureHeaders headers = sut.Sign(
            "id",
            Now.ToUnixTimeSeconds(),
            Encoding.UTF8.GetBytes("body"),
            [primary, secondary]
        );

        headers.Signature.Split(' ').Should().HaveCount(2);
        headers.Signature.Split(' ').Should().OnlyContain(s => s.StartsWith("v1,"));
    }
}
