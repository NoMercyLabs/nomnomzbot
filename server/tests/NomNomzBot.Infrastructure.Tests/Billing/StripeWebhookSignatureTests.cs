// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NomNomzBot.Infrastructure.Billing;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the Stripe webhook signature boundary (monetization-billing.md §5.2): a correctly-signed payload within
/// tolerance is accepted, while a tampered body, wrong secret, stale timestamp, or missing header are all
/// rejected — the gate that lets the webhook controller trust an anonymous request.
/// </summary>
public sealed class StripeWebhookSignatureTests
{
    private const string Secret = "whsec_test_secret";
    private const long Now = 1_700_000_000;
    private const string Payload = "{\"id\":\"evt_1\",\"type\":\"customer.subscription.updated\"}";

    private static string SignedHeader(string payload, long timestamp, string secret)
    {
        byte[] signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")
        );
        return $"t={timestamp},v1={Convert.ToHexString(signature).ToLowerInvariant()}";
    }

    [Fact]
    public void A_correctly_signed_payload_within_tolerance_is_accepted()
    {
        string header = SignedHeader(Payload, Now, Secret);
        StripeWebhookSignature.Verify(Payload, header, Secret, Now).Should().BeTrue();
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        string header = SignedHeader(Payload, Now, Secret);
        StripeWebhookSignature
            .Verify(Payload.Replace("evt_1", "evt_HACKED"), header, Secret, Now)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void A_wrong_secret_is_rejected()
    {
        string header = SignedHeader(Payload, Now, Secret);
        StripeWebhookSignature.Verify(Payload, header, "whsec_wrong", Now).Should().BeFalse();
    }

    [Fact]
    public void A_stale_timestamp_outside_tolerance_is_rejected()
    {
        string header = SignedHeader(Payload, Now, Secret);
        StripeWebhookSignature.Verify(Payload, header, Secret, Now + 10_000).Should().BeFalse();
    }

    [Fact]
    public void A_missing_or_empty_header_is_rejected()
    {
        StripeWebhookSignature.Verify(Payload, null, Secret, Now).Should().BeFalse();
        StripeWebhookSignature.Verify(Payload, "", Secret, Now).Should().BeFalse();
    }
}
