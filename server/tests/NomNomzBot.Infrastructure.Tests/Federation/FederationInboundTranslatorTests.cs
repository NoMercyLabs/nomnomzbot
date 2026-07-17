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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.DTOs.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Infrastructure.Federation;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves the inbound translator (federation-oidc.md §3.6/§3.7): an unrecognized event type fails closed
/// (<c>schema_invalid</c>); a directory-broadcast envelope fans out to EVERY channel that opted in and to no
/// other; a directed envelope applies only to its named target when that target opted in; an untrusted peer
/// reaches no channel; and a handler-level payload failure propagates as <c>schema_invalid</c>.
/// </summary>
public sealed class FederationInboundTranslatorTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000000a2");
    private static readonly Guid ChannelC = Guid.Parse("0192a000-0000-7000-8000-0000000000a3");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-0000000000ff");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private const string BanType = "moderation.ban.shared";

    private static FederationEventEnvelope Envelope(Guid? target) =>
        new(
            Guid.CreateVersion7(),
            "peer-1",
            OriginBroadcasterId: null,
            TargetBroadcasterId: target,
            BanType,
            SchemaVersion: 1,
            PayloadJson: "{}",
            OccurredAt: Now
        );

    private static (
        FederationInboundTranslator Sut,
        FederationOptInService OptIns,
        RecordingHandler Handler
    ) Build(AuthDbContext db, Result? handlerResult = null)
    {
        FederationOptInService optIns = new(db, new RecordingEventBus(), new FakeTimeProvider(Now));
        RecordingHandler handler = new(handlerResult ?? Result.Success());
        FederationInboundTranslator sut = new([handler], optIns);
        return (sut, optIns, handler);
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

    private static Task AcceptAsync(FederationOptInService optIns, Guid channel, Guid? peer) =>
        optIns.UpsertAsync(
            channel,
            new UpsertChannelFederationOptInRequest(
                peer,
                FederationOptInType.SharedChatBans,
                FederationDirection.Accept,
                IsEnabled: true
            ),
            Actor
        );

    [Fact]
    public async Task Unrecognized_event_type_fails_closed_as_schema_invalid()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (FederationInboundTranslator sut, _, RecordingHandler handler) = Build(db);
        Guid peer = await SeedTrustedPeerAsync(db);

        FederationEventEnvelope unknown = Envelope(null) with
        {
            FederatedEventType = "trust.list.updated",
        };
        Result<int> result = await sut.TranslateAndApplyAsync(peer, unknown);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("schema_invalid");
        handler.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Broadcast_fans_out_to_every_opted_in_channel_and_no_other()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (FederationInboundTranslator sut, FederationOptInService optIns, RecordingHandler handler) =
            Build(db);
        Guid peer = await SeedTrustedPeerAsync(db);
        await AcceptAsync(optIns, ChannelA, peer);
        await AcceptAsync(optIns, ChannelB, null); // "any trusted peer" wildcard
        // ChannelC never opted in.

        Result<int> result = await sut.TranslateAndApplyAsync(peer, Envelope(null));

        result.Value.Should().Be(2);
        handler.Invocations.Select(i => i.Target).Should().BeEquivalentTo([ChannelA, ChannelB]);
        handler.Invocations.Should().NotContain(i => i.Target == ChannelC);
        handler.Invocations.Should().OnlyContain(i => i.Peer == peer);
    }

    [Fact]
    public async Task Directed_envelope_applies_only_to_its_named_target_when_it_opted_in()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (FederationInboundTranslator sut, FederationOptInService optIns, RecordingHandler handler) =
            Build(db);
        Guid peer = await SeedTrustedPeerAsync(db);
        await AcceptAsync(optIns, ChannelA, peer);

        Result<int> toOptedIn = await sut.TranslateAndApplyAsync(peer, Envelope(ChannelA));
        toOptedIn.Value.Should().Be(1);
        handler.Invocations.Should().ContainSingle(i => i.Target == ChannelA);

        // A directed envelope at a channel with no opt-in resolves to zero targets (an accepted noop here;
        // the gateway is what turns a directed miss into a no_opt_in rejection).
        Result<int> toStranger = await sut.TranslateAndApplyAsync(peer, Envelope(ChannelB));
        toStranger.Value.Should().Be(0);
        handler.Invocations.Should().NotContain(i => i.Target == ChannelB);
    }

    [Fact]
    public async Task An_untrusted_peer_reaches_no_channel_even_with_a_wildcard_opt_in()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (FederationInboundTranslator sut, FederationOptInService optIns, RecordingHandler handler) =
            Build(db);
        Guid peer = await SeedTrustedPeerAsync(db, FederationTrustState.Revoked);
        await AcceptAsync(optIns, ChannelA, null);

        Result<int> result = await sut.TranslateAndApplyAsync(peer, Envelope(null));

        result.Value.Should().Be(0);
        handler.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_handler_payload_failure_propagates_as_schema_invalid()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (FederationInboundTranslator sut, FederationOptInService optIns, RecordingHandler handler) =
            Build(db, Result.Failure("bad payload", "schema_invalid"));
        Guid peer = await SeedTrustedPeerAsync(db);
        await AcceptAsync(optIns, ChannelA, peer);

        Result<int> result = await sut.TranslateAndApplyAsync(peer, Envelope(null));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("schema_invalid");
        handler.Invocations.Should().ContainSingle(); // it did attempt the one resolved target
    }

    /// <summary>A handler that records every (peer, target) it was asked to apply, returning a fixed result.</summary>
    private sealed class RecordingHandler(Result result) : IFederationInboundHandler
    {
        public List<(Guid Peer, Guid Target)> Invocations { get; } = [];

        public string Type => BanType;

        public string GatingOptInType => FederationOptInType.SharedChatBans;

        public Task<Result> ApplyAsync(
            Guid peerId,
            Guid targetBroadcasterId,
            FederationEventEnvelope envelope,
            CancellationToken cancellationToken = default
        )
        {
            Invocations.Add((peerId, targetBroadcasterId));
            return Task.FromResult(result);
        }
    }
}
