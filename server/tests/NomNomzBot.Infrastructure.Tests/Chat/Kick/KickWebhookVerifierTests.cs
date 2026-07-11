// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Infrastructure.Chat.Kick;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick webhook signature check with REAL RSA end-to-end (a test keypair plays Kick): a
/// delivery signed over <c>{id}.{ts}.{body}</c> with SHA-256/PKCS#1 v1.5 verifies against the fetched
/// PEM key; any tampering (body, id, timestamp) fails; garbage signatures fail without throwing; and a
/// key ROTATION self-heals — a miss with a cached key triggers one refetch and verifies against the new
/// key. The public key is fetched once and cached (call-count asserted), so a webhook burst does not
/// hammer Kick's key endpoint.
/// </summary>
public sealed class KickWebhookVerifierTests
{
    private const string MessageId = "01JCULID000000000000000000";
    private const string Timestamp = "2026-07-11T12:00:00Z";
    private const string Body = """{"message_id":"a1","content":"hello"}""";

    private static byte[] Sign(RSA key, string messageId, string timestamp, string body) =>
        key.SignData(
            Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{body}"),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

    private static KickWebhookVerifier Build(CountingKeyHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("kick").Returns(new HttpClient(handler));
        return new KickWebhookVerifier(
            factory,
            TimeProvider.System,
            NullLogger<KickWebhookVerifier>.Instance
        );
    }

    [Fact]
    public async Task A_genuinely_signed_delivery_verifies_and_the_key_is_cached()
    {
        using RSA kick = RSA.Create(2048);
        CountingKeyHandler handler = new(kick.ExportRSAPublicKeyPem());
        KickWebhookVerifier sut = Build(handler);
        string signature = Convert.ToBase64String(Sign(kick, MessageId, Timestamp, Body));

        (await sut.VerifyAsync(MessageId, Timestamp, Body, signature)).Should().BeTrue();
        (await sut.VerifyAsync(MessageId, Timestamp, Body, signature)).Should().BeTrue();

        handler.FetchCount.Should().Be(1, "the PEM key must be cached across deliveries");
    }

    [Fact]
    public async Task Any_tampering_with_the_signed_triple_fails()
    {
        using RSA kick = RSA.Create(2048);
        CountingKeyHandler handler = new(kick.ExportRSAPublicKeyPem());
        KickWebhookVerifier sut = Build(handler);
        string signature = Convert.ToBase64String(Sign(kick, MessageId, Timestamp, Body));

        // The id, the timestamp, and the body are all INSIDE the signature — forging any one breaks it.
        (await sut.VerifyAsync("01OTHER", Timestamp, Body, signature))
            .Should()
            .BeFalse();
        (await sut.VerifyAsync(MessageId, "2026-07-11T13:00:00Z", Body, signature))
            .Should()
            .BeFalse();
        (await sut.VerifyAsync(MessageId, Timestamp, """{"forged":true}""", signature))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Garbage_signatures_fail_closed_without_throwing()
    {
        using RSA kick = RSA.Create(2048);
        CountingKeyHandler handler = new(kick.ExportRSAPublicKeyPem());
        KickWebhookVerifier sut = Build(handler);

        (await sut.VerifyAsync(MessageId, Timestamp, Body, "not-base64!!")).Should().BeFalse();
        (await sut.VerifyAsync(MessageId, Timestamp, Body, Convert.ToBase64String([1, 2, 3, 4])))
            .Should()
            .BeFalse();
        (await sut.VerifyAsync("", Timestamp, Body, "sig")).Should().BeFalse();
    }

    [Fact]
    public async Task A_key_rotation_self_heals_with_one_forced_refetch()
    {
        using RSA oldKey = RSA.Create(2048);
        using RSA newKey = RSA.Create(2048);
        // First fetch serves the OLD key; the second (forced by the verify miss) serves the NEW one.
        CountingKeyHandler handler = new(
            oldKey.ExportRSAPublicKeyPem(),
            newKey.ExportRSAPublicKeyPem()
        );
        KickWebhookVerifier sut = Build(handler);

        // Prime the cache with the old key.
        string oldSigned = Convert.ToBase64String(Sign(oldKey, MessageId, Timestamp, Body));
        (await sut.VerifyAsync(MessageId, Timestamp, Body, oldSigned)).Should().BeTrue();

        // Kick rotates: a delivery signed with the NEW key must still verify (one refetch), not drop.
        string newSigned = Convert.ToBase64String(Sign(newKey, MessageId, Timestamp, Body));
        (await sut.VerifyAsync(MessageId, Timestamp, Body, newSigned)).Should().BeTrue();
        handler.FetchCount.Should().Be(2);
    }

    /// <summary>Serves the queued PEM keys as Kick's public-key endpoint and counts fetches (proves
    /// caching + the single rotation refetch).</summary>
    private sealed class CountingKeyHandler(params string[] pems) : HttpMessageHandler
    {
        private int _index;

        public int FetchCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            FetchCount++;
            string pem = pems[Math.Min(_index++, pems.Length - 1)];
            string json = JsonSerializer.Serialize(new { data = new { public_key = pem } });
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
