// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.DTOs.Federation;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Infrastructure.Federation;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves the federation trust directory (federation-oidc.md §3.1): register is idempotent on InstanceId and lands
/// a peer pending; trust requires an active rsa-sha256 key (an ed25519-only peer is rejected) and emits the
/// trusted event; revoke flips trust + deactivates every key + emits; and a duplicate KeyId is rejected.
/// </summary>
public sealed class FederationPeerServiceTests
{
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000f11");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FederationPeerService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        return (new FederationPeerService(db, bus, new FakeTimeProvider(Now)), db, bus);
    }

    private static RegisterFederationPeerRequest Req(
        string algorithm = FederationKeyAlgorithm.RsaSha256,
        string instanceId = "peer-1",
        string keyId = "k1"
    ) =>
        new(
            instanceId,
            "Peer One",
            "https://peer.example",
            "self_host_full",
            "PUBKEY",
            keyId,
            algorithm
        );

    [Fact]
    public async Task Register_then_trust_with_an_rsa_key_emits_the_trusted_event()
    {
        (FederationPeerService sut, _, RecordingEventBus bus) = Build();

        FederationPeerDto registered = (await sut.RegisterPeerAsync(Req())).Value;
        registered.TrustState.Should().Be("pending");

        FederationPeerDto trusted = (await sut.TrustPeerAsync(registered.Id, Actor)).Value;

        trusted.TrustState.Should().Be("trusted");
        trusted.LastHandshakeAt.Should().NotBeNull();
        bus.Published.OfType<FederationPeerTrustedEvent>()
            .Should()
            .ContainSingle(e => e.PeerId == registered.Id);
    }

    [Fact]
    public async Task Register_is_idempotent_on_instance_id()
    {
        (FederationPeerService sut, AuthDbContext db, _) = Build();

        Guid first = (await sut.RegisterPeerAsync(Req())).Value.Id;
        Guid second = (await sut.RegisterPeerAsync(Req())).Value.Id;

        second.Should().Be(first);
        db.FederationPeers.Count().Should().Be(1);
    }

    [Fact]
    public async Task Trust_fails_when_the_peer_has_no_rsa_key()
    {
        (FederationPeerService sut, _, _) = Build();
        FederationPeerDto registered = (
            await sut.RegisterPeerAsync(Req(algorithm: FederationKeyAlgorithm.Ed25519))
        ).Value;

        (await sut.TrustPeerAsync(registered.Id, Actor)).ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Revoke_flips_trust_deactivates_keys_and_emits()
    {
        (FederationPeerService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        FederationPeerDto registered = (await sut.RegisterPeerAsync(Req())).Value;
        await sut.TrustPeerAsync(registered.Id, Actor);

        (
            await sut.RevokePeerAsync(
                registered.Id,
                new RevokeFederationPeerRequest("manual", Blocked: false),
                Actor
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        db.FederationPeers.Single().TrustState.Should().Be("revoked");
        db.FederationPeerKeys.Where(k => k.PeerId == registered.Id)
            .All(k => !k.IsActive)
            .Should()
            .BeTrue();
        bus.Published.OfType<FederationPeerRevokedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task AddPeerKey_rejects_a_duplicate_key_id()
    {
        (FederationPeerService sut, _, _) = Build();
        FederationPeerDto registered = (await sut.RegisterPeerAsync(Req(keyId: "k1"))).Value;

        (
            await sut.AddPeerKeyAsync(
                registered.Id,
                new AddFederationPeerKeyRequest(
                    "PUB2",
                    "k1",
                    FederationKeyAlgorithm.RsaSha256,
                    Now.UtcDateTime,
                    null
                )
            )
        )
            .ErrorCode.Should()
            .Be("ALREADY_EXISTS");
    }
}
