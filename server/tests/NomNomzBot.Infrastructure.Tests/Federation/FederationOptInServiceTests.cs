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
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Infrastructure.Federation;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves per-channel federation opt-in (federation-oidc.md §3.4): an upsert is the explicit allow and emits the
/// change event; the permit predicate is default-deny — it requires BOTH a trusted peer AND an enabled, direction-
/// matching opt-in (honoring the null-peer "any trusted" wildcard); and disable soft-deletes + emits.
/// </summary>
public sealed class FederationOptInServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000f02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FederationOptInService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        return (new FederationOptInService(db, bus, new FakeTimeProvider(Now)), db, bus);
    }

    private static async Task<Guid> SeedTrustedPeerAsync(AuthDbContext db, string trust = "trusted")
    {
        FederationPeer peer = new()
        {
            InstanceId = "peer-1",
            DeploymentMode = "self_host_full",
            TrustState = trust,
            FirstSeenAt = Now.UtcDateTime,
        };
        db.FederationPeers.Add(peer);
        await db.SaveChangesAsync();
        return peer.Id;
    }

    [Fact]
    public async Task Upsert_creates_the_opt_in_and_emits_the_change_event()
    {
        (FederationOptInService sut, _, RecordingEventBus bus) = Build();

        ChannelFederationOptInDto dto = (
            await sut.UpsertAsync(
                Channel,
                new UpsertChannelFederationOptInRequest(
                    null,
                    FederationOptInType.SharedChatBans,
                    FederationDirection.Both,
                    IsEnabled: true
                ),
                Actor
            )
        ).Value;

        dto.OptInType.Should().Be("shared_chat_bans");
        dto.IsEnabled.Should().BeTrue();
        (await sut.ListAsync(Channel)).Value.Should().ContainSingle();
        bus.Published.OfType<ChannelFederationOptInChangedEvent>()
            .Should()
            .ContainSingle(e => e.IsEnabled && e.OptInType == "shared_chat_bans");
    }

    [Fact]
    public async Task IsActionPermitted_requires_both_a_trusted_peer_and_a_matching_opt_in()
    {
        (FederationOptInService sut, AuthDbContext db, _) = Build();
        Guid peer = await SeedTrustedPeerAsync(db);
        await sut.UpsertAsync(
            Channel,
            new UpsertChannelFederationOptInRequest(
                peer,
                FederationOptInType.SharedChatBans,
                FederationDirection.Both,
                IsEnabled: true
            ),
            Actor
        );

        (
            await sut.IsActionPermittedAsync(
                Channel,
                peer,
                FederationOptInType.SharedChatBans,
                FederationDirection.Accept
            )
        )
            .Value.Should()
            .BeTrue();
        // A type the channel never opted into is denied.
        (
            await sut.IsActionPermittedAsync(
                Channel,
                peer,
                FederationOptInType.SharedSavings,
                FederationDirection.Accept
            )
        )
            .Value.Should()
            .BeFalse();
    }

    [Fact]
    public async Task IsActionPermitted_denies_an_untrusted_peer_even_with_an_opt_in()
    {
        (FederationOptInService sut, AuthDbContext db, _) = Build();
        Guid peer = await SeedTrustedPeerAsync(db, FederationTrustState.Revoked);
        await sut.UpsertAsync(
            Channel,
            new UpsertChannelFederationOptInRequest(
                null, // any trusted peer
                FederationOptInType.SharedChatBans,
                FederationDirection.Both,
                IsEnabled: true
            ),
            Actor
        );

        (
            await sut.IsActionPermittedAsync(
                Channel,
                peer,
                FederationOptInType.SharedChatBans,
                FederationDirection.Accept
            )
        )
            .Value.Should()
            .BeFalse();
    }

    [Fact]
    public async Task Disable_soft_deletes_and_emits_a_disabled_event()
    {
        (FederationOptInService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        ChannelFederationOptInDto created = (
            await sut.UpsertAsync(
                Channel,
                new UpsertChannelFederationOptInRequest(
                    null,
                    FederationOptInType.SharedChatBans,
                    FederationDirection.Both,
                    IsEnabled: true
                ),
                Actor
            )
        ).Value;

        (await sut.DisableAsync(Channel, created.Id, Actor)).IsSuccess.Should().BeTrue();

        (await sut.ListAsync(Channel)).Value.Should().BeEmpty(); // soft-deleted
        bus.Published.OfType<ChannelFederationOptInChangedEvent>()
            .Should()
            .Contain(e => !e.IsEnabled);
    }
}
