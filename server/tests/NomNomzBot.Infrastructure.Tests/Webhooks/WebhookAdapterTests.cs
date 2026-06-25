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
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Infrastructure.Webhooks;
using NomNomzBot.Infrastructure.Webhooks.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the inbound adapters (webhooks.md §3.3): Ko-fi compares the body verification token; GitHub verifies the
/// X-Hub-Signature-256 HMAC + reads its kind/id headers; the generic adapter verifies a hex HMAC over the templated
/// signing string (with the timestamp replay guard) or a shared-secret body field, and parses kind/id via JSONPath.
/// Each rejects a wrong secret.
/// </summary>
public sealed class WebhookAdapterTests
{
    private static readonly DateTime Now = new(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc);

    private static InboundSignatureVerifier Verifier() =>
        new(new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

    private static InboundWebhookRequest Request(
        byte[] body,
        string contentType = "application/json",
        Dictionary<string, string>? headers = null
    ) =>
        new()
        {
            Token = "t",
            Method = "POST",
            ContentType = contentType,
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            RawBody = body,
            ReceivedAtUtc = Now,
            RemoteIpHash = "iphash",
        };

    [Fact]
    public void Kofi_verifies_the_body_token_and_parses_the_event()
    {
        KofiInboundWebhookAdapter sut = new();
        string inner =
            "{\"verification_token\":\"kofi-secret\",\"type\":\"Donation\",\"kofi_transaction_id\":\"tx_1\",\"from_name\":\"Alice\"}";
        InboundWebhookRequest req = Request(
            Encoding.UTF8.GetBytes("data=" + Uri.EscapeDataString(inner)),
            "application/x-www-form-urlencoded"
        );

        sut.Verify(req, Encoding.UTF8.GetBytes("kofi-secret"), null)
            .Value.IsValid.Should()
            .BeTrue();
        sut.Verify(req, Encoding.UTF8.GetBytes("wrong"), null).Value.IsValid.Should().BeFalse();

        ParsedInboundEvent parsed = sut.Parse(req, null).Value;
        parsed.Kind.Should().Be("donation");
        parsed.ProviderEventId.Should().Be("tx_1");
        parsed.Variables["from_name"].Should().Be("Alice");
    }

    [Fact]
    public void Github_verifies_the_hub_signature_and_parses_headers()
    {
        GithubInboundWebhookAdapter sut = new(Verifier());
        byte[] body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        byte[] secret = Encoding.UTF8.GetBytes("gh-secret");
        string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, body));
        InboundWebhookRequest req = Request(
            body,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Hub-Signature-256"] = signature,
                ["X-GitHub-Event"] = "pull_request",
                ["X-GitHub-Delivery"] = "del_1",
            }
        );

        sut.Verify(req, secret, null).Value.IsValid.Should().BeTrue();
        sut.Verify(req, Encoding.UTF8.GetBytes("wrong"), null).Value.IsValid.Should().BeFalse();

        ParsedInboundEvent parsed = sut.Parse(req, null).Value;
        parsed.Kind.Should().Be("pull_request");
        parsed.ProviderEventId.Should().Be("del_1");
        parsed.Variables["action"].Should().Be("opened");
    }

    [Fact]
    public void Generic_hmac_verifies_with_timestamp_and_parses_via_jsonpath()
    {
        GenericInboundWebhookAdapter sut = new(Verifier());
        GenericInboundConfig config = new(
            "X-Signature",
            "sha256=",
            "{id}.{timestamp}.{body}",
            "X-Timestamp",
            null,
            "$.event.type",
            "$.event.id"
        );
        byte[] body = Encoding.UTF8.GetBytes(
            "{\"event\":{\"type\":\"order.created\",\"id\":\"evt_9\"}}"
        );
        byte[] secret = Encoding.UTF8.GetBytes("gen-secret");
        long ts = new DateTimeOffset(Now, TimeSpan.Zero).ToUnixTimeSeconds();
        string signingString = $"wh_1.{ts}.{Encoding.UTF8.GetString(body)}";
        string signature =
            "sha256="
            + Convert.ToHexStringLower(
                HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(signingString))
            );
        InboundWebhookRequest req = Request(
            body,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Signature"] = signature,
                ["X-Timestamp"] = ts.ToString(),
                ["webhook-id"] = "wh_1",
            }
        );

        sut.Verify(req, secret, config).Value.IsValid.Should().BeTrue();
        sut.Verify(req, Encoding.UTF8.GetBytes("wrong"), config).Value.IsValid.Should().BeFalse();

        ParsedInboundEvent parsed = sut.Parse(req, config).Value;
        parsed.Kind.Should().Be("order.created");
        parsed.ProviderEventId.Should().Be("evt_9");
    }

    [Fact]
    public void Generic_shared_secret_mode_verifies_a_body_field()
    {
        GenericInboundWebhookAdapter sut = new(Verifier());
        GenericInboundConfig config = new(null, null, null, null, "secret_field", "$.type", "$.id");
        byte[] body = Encoding.UTF8.GetBytes(
            "{\"secret_field\":\"shh\",\"type\":\"ping\",\"id\":\"1\"}"
        );

        sut.Verify(Request(body), Encoding.UTF8.GetBytes("shh"), config)
            .Value.IsValid.Should()
            .BeTrue();
        sut.Verify(Request(body), Encoding.UTF8.GetBytes("nope"), config)
            .Value.IsValid.Should()
            .BeFalse();
    }
}
