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
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Infrastructure.Federation;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves the federation signature boundary (federation-oidc.md §3.3): a signed envelope round-trips against the
/// peer's stored public key, while a tampered envelope, a non-rsa-sha256 algorithm, and an inactive key are all
/// rejected with their distinct reasons — the gate every inbound peer event must pass.
/// </summary>
public sealed class FederationEventSignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private const string KeyId = "instance-key-1";

    private static (FederationEventSigner Sut, AuthDbContext Db, RSA Rsa) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RSA rsa = RSA.Create(2048);
        IFederationSigningKeyProvider keyProvider = Substitute.For<IFederationSigningKeyProvider>();
        keyProvider
            .GetActiveSigningKey()
            .Returns(Result.Success(new FederationSigningKey(KeyId, rsa.ExportRSAPrivateKeyPem())));
        return (new FederationEventSigner(db, keyProvider, new FakeTimeProvider(Now)), db, rsa);
    }

    private static async Task<Guid> SeedPeerKeyAsync(
        AuthDbContext db,
        RSA rsa,
        bool active = true,
        string algorithm = FederationKeyAlgorithm.RsaSha256
    )
    {
        FederationPeer peer = new()
        {
            InstanceId = "peer-x",
            DeploymentMode = "self_host_full",
            TrustState = FederationTrustState.Trusted,
            FirstSeenAt = Now.UtcDateTime,
        };
        db.FederationPeers.Add(peer);
        db.FederationPeerKeys.Add(
            new FederationPeerKey
            {
                PeerId = peer.Id,
                PublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
                Algorithm = algorithm,
                KeyId = KeyId,
                ValidFrom = Now.UtcDateTime.AddDays(-1),
                IsActive = active,
            }
        );
        await db.SaveChangesAsync();
        return peer.Id;
    }

    private static FederationEventEnvelope Envelope(string payload = "{\"userId\":\"123\"}") =>
        new(
            Guid.Parse("0192a000-0000-7000-8000-000000000fff"),
            "origin-instance",
            null,
            null,
            "moderation.ban.shared",
            1,
            payload,
            Now
        );

    [Fact]
    public async Task A_signed_envelope_round_trips()
    {
        (FederationEventSigner sut, AuthDbContext db, RSA rsa) = Build();
        Guid peer = await SeedPeerKeyAsync(db, rsa);
        FederationEventEnvelope envelope = Envelope();

        FederationSignature signature = (await sut.SignAsync(envelope)).Value;

        signature.Algorithm.Should().Be("rsa-sha256");
        signature.KeyId.Should().Be(KeyId);
        (await sut.VerifyAsync(peer, envelope, signature)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_tampered_envelope_fails_verification()
    {
        (FederationEventSigner sut, AuthDbContext db, RSA rsa) = Build();
        Guid peer = await SeedPeerKeyAsync(db, rsa);
        FederationSignature signature = (await sut.SignAsync(Envelope())).Value;

        Result tampered = await sut.VerifyAsync(
            peer,
            Envelope("{\"userId\":\"HACKED\"}"),
            signature
        );

        tampered.ErrorCode.Should().Be("signature_invalid");
    }

    [Fact]
    public async Task A_non_rsa_algorithm_is_rejected_distinctly()
    {
        (FederationEventSigner sut, AuthDbContext db, RSA rsa) = Build();
        Guid peer = await SeedPeerKeyAsync(db, rsa);
        FederationSignature signature = (await sut.SignAsync(Envelope())).Value;

        Result result = await sut.VerifyAsync(
            peer,
            Envelope(),
            signature with
            {
                Algorithm = FederationKeyAlgorithm.Ed25519,
            }
        );

        result.ErrorCode.Should().Be("algorithm_unsupported");
    }

    [Fact]
    public async Task An_inactive_key_is_rejected()
    {
        (FederationEventSigner sut, AuthDbContext db, RSA rsa) = Build();
        Guid peer = await SeedPeerKeyAsync(db, rsa, active: false);
        FederationSignature signature = (await sut.SignAsync(Envelope())).Value;

        (await sut.VerifyAsync(peer, Envelope(), signature)).ErrorCode.Should().Be("key_unknown");
    }
}
