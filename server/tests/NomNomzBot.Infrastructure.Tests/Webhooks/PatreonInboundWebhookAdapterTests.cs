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
/// Proves the Patreon inbound adapter (webhooks.md §3.3) verifies <c>X-Patreon-Signature</c> as a <b>hex</b>
/// HMAC-<b>MD5</b> over the raw body — a different algorithm and encoding from the base64-SHA256 providers —
/// and that Parse flattens the JSON:API body while injecting the header-only <c>X-Patreon-Event</c> as
/// <c>patreon.event</c> so the supporter source can gate a new pledge apart from a cancellation.
/// </summary>
public sealed class PatreonInboundWebhookAdapterTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("patreon-webhook-secret");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"data":{"type":"member","id":"m1","attributes":{"currently_entitled_amount_cents":500,"full_name":"Pat"}}}"""
    );

    private readonly PatreonInboundWebhookAdapter _adapter = new();

    private static InboundWebhookRequest Request(
        string? signature,
        string patreonEvent = "members:pledge:create"
    )
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Patreon-Event"] = patreonEvent,
        };
        if (signature is not null)
            headers["X-Patreon-Signature"] = signature;
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
        Convert.ToHexStringLower(HMACMD5.HashData(Secret, Body));

    [Fact]
    public void Verify_CorrectHexMd5Hmac_IsValid()
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
        // Patreon signs in hex, not base64 — accepting base64 would mean the encoding is not actually pinned.
        string base64 = Convert.ToBase64String(HMACMD5.HashData(Secret, Body));

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
    public void Parse_InjectsTheHeaderEventAndFlattensTheJsonApiBody()
    {
        Result<ParsedInboundEvent> result = _adapter.Parse(Request(ValidSignature()), null);

        result.IsSuccess.Should().BeTrue();
        // The header-only trigger is carried into the bag verbatim for the supporter source to gate on.
        result.Value.Variables["patreon.event"].Should().Be("members:pledge:create");
        result.Value.Kind.Should().Be("members.pledge.create"); // ':' normalized for the journal label
        result
            .Value.Variables["data.attributes.currently_entitled_amount_cents"]
            .Should()
            .Be("500");
        // No native event id → dedupe composite of member id + last charge.
        result.Value.ProviderEventId.Should().Be("m1:none");
    }
}
