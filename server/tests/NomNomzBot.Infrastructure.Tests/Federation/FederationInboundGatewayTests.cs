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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Infrastructure.Federation;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves the inbound gateway's fail-closed gate sequence (federation-oidc.md §3.5). A valid envelope is
/// journaled with <c>Source=federation</c>, applied, and announced with <see cref="FederatedEventReceivedEvent"/>;
/// a bad signature, an untrusted peer, a replay, an unknown type, and a directed miss are each rejected with the
/// right reason on <see cref="FederatedEventRejectedEvent"/>, never journaled, and never dispatched.
/// </summary>
public sealed class FederationInboundGatewayTests
{
    private static readonly Guid DirectedChannel = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000d1"
    );
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private const string BanType = "moderation.ban.shared";
    private const string KeyId = "k1";

    private sealed class Harness
    {
        public required AuthDbContext Db { get; init; }
        public required FederationInboundGateway Sut { get; init; }
        public required FederationEventSigner Signer { get; init; }
        public required FakeEventJournal Journal { get; init; }
        public required FakeTranslator Translator { get; init; }
        public required RecordingEventBus Bus { get; init; }
        public required Guid PeerId { get; init; }
        public required string PublicPem { get; init; }
    }

    private static async Task<Harness> BuildAsync(
        Result<int>? translatorResult = null,
        string peerTrust = FederationTrustState.Trusted,
        string peerInstanceId = "peer-1"
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new(Now);

        using RSA rsa = RSA.Create(2048);
        string privatePem = rsa.ExportRSAPrivateKeyPem();
        string publicPem = rsa.ExportSubjectPublicKeyInfoPem();

        FederationPeer peer = new()
        {
            InstanceId = peerInstanceId,
            DeploymentMode = "self_host_full",
            TrustState = peerTrust,
            FirstSeenAt = Now.UtcDateTime,
        };
        db.FederationPeers.Add(peer);
        db.FederationPeerKeys.Add(
            new FederationPeerKey
            {
                PeerId = peer.Id,
                PublicKey = publicPem,
                Algorithm = FederationKeyAlgorithm.RsaSha256,
                KeyId = KeyId,
                ValidFrom = Now.UtcDateTime.AddDays(-1),
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();

        FederationEventSigner signer = new(
            db,
            new FakeSigningKeyProvider(KeyId, privatePem),
            clock
        );
        FakeEventJournal journal = new();
        FakeTranslator translator = new(translatorResult ?? Result.Success(1));
        RecordingEventBus bus = new();
        FederationOptInService optIns = new(db, new RecordingEventBus(), clock);
        FakeHandler handler = new();

        FederationInboundGateway sut = new(db, signer, optIns, translator, journal, bus, [handler]);

        return new Harness
        {
            Db = db,
            Sut = sut,
            Signer = signer,
            Journal = journal,
            Translator = translator,
            Bus = bus,
            PeerId = peer.Id,
            PublicPem = publicPem,
        };
    }

    private static FederationEventEnvelope Envelope(Guid? target, string type = BanType) =>
        new(
            Guid.CreateVersion7(),
            "peer-1",
            OriginBroadcasterId: null,
            TargetBroadcasterId: target,
            type,
            SchemaVersion: 1,
            PayloadJson: """{"targetTwitchUserId":"9001"}""",
            OccurredAt: Now
        );

    private async Task<FederationSignature> SignAsync(
        FederationEventSigner signer,
        FederationEventEnvelope envelope
    ) => (await signer.SignAsync(envelope)).Value;

    [Fact]
    public async Task A_valid_envelope_is_journaled_as_federation_applied_and_announced()
    {
        Harness h = await BuildAsync(translatorResult: Result.Success(2));
        FederationEventEnvelope envelope = Envelope(null);
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.EventId.Should().Be(envelope.EventId);
        result.Value.StreamPosition.Should().BeGreaterThan(0);
        result.Value.Applied.Should().BeTrue();

        // Journaled once, as a federation-sourced row keyed on the envelope's dedupe id.
        h.Journal.Appended.Should().ContainSingle();
        AppendEventRequest appended = h.Journal.Appended[0];
        appended.EventId.Should().Be(envelope.EventId);
        appended.Source.Should().Be("federation");
        appended.EventType.Should().Be(BanType);

        // The received-claim event carries the journal id + allocated position (not its own event id).
        FederatedEventReceivedEvent received = h
            .Bus.Published.OfType<FederatedEventReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        received.JournalEventId.Should().Be(envelope.EventId);
        received.StreamPosition.Should().Be(result.Value.StreamPosition);
        received.PeerId.Should().Be(h.PeerId);
        h.Bus.Published.OfType<FederatedEventRejectedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Zero_matching_channels_is_an_accepted_noop_still_journaled()
    {
        Harness h = await BuildAsync(translatorResult: Result.Success(0));
        FederationEventEnvelope envelope = Envelope(null);
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Applied.Should().BeFalse(); // accepted, but no channel applied it
        h.Journal.Appended.Should().ContainSingle();
        h.Bus.Published.OfType<FederatedEventReceivedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_bad_signature_is_rejected_signature_invalid_and_never_journaled()
    {
        Harness h = await BuildAsync();
        FederationEventEnvelope envelope = Envelope(null);
        // A signature produced over a DIFFERENT envelope will not verify against this one.
        FederationSignature wrong = await SignAsync(h.Signer, Envelope(null));

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            wrong
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("signature_invalid");
        h.Journal.Appended.Should().BeEmpty();
        h.Translator.Calls.Should().Be(0);
        h.Bus.Published.OfType<FederatedEventRejectedEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "signature_invalid");
    }

    [Fact]
    public async Task An_untrusted_peer_is_rejected_peer_untrusted()
    {
        Harness h = await BuildAsync(peerTrust: FederationTrustState.Revoked);
        FederationEventEnvelope envelope = Envelope(null);
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.ErrorCode.Should().Be("peer_untrusted");
        h.Journal.Appended.Should().BeEmpty();
        h.Translator.Calls.Should().Be(0);
        h.Bus.Published.OfType<FederatedEventRejectedEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "peer_untrusted");
    }

    [Fact]
    public async Task A_relayed_envelope_whose_instance_id_mismatches_is_rejected_peer_untrusted()
    {
        // The peer is trusted, but its stored InstanceId is "other" while the envelope claims "peer-1".
        Harness h = await BuildAsync(peerInstanceId: "other-instance");
        FederationEventEnvelope envelope = Envelope(null);
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.ErrorCode.Should().Be("peer_untrusted");
        h.Journal.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task An_already_journaled_event_id_is_rejected_as_replay()
    {
        Harness h = await BuildAsync();
        FederationEventEnvelope envelope = Envelope(null);
        FederationSignature signature = await SignAsync(h.Signer, envelope);
        h.Journal.Seed(envelope.EventId); // as if a prior delivery already landed

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.ErrorCode.Should().Be("replay");
        h.Journal.Appended.Should().BeEmpty(); // no second append
        h.Translator.Calls.Should().Be(0);
        h.Bus.Published.OfType<FederatedEventRejectedEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "replay");
    }

    [Fact]
    public async Task An_unrecognized_type_is_rejected_schema_invalid_before_journaling()
    {
        Harness h = await BuildAsync();
        FederationEventEnvelope envelope = Envelope(null, type: "savings.contribution");
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.ErrorCode.Should().Be("schema_invalid");
        h.Journal.Appended.Should().BeEmpty();
        h.Translator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_directed_envelope_at_a_channel_that_did_not_opt_in_is_rejected_no_opt_in()
    {
        Harness h = await BuildAsync();
        FederationEventEnvelope envelope = Envelope(DirectedChannel); // no opt-in seeded for it
        FederationSignature signature = await SignAsync(h.Signer, envelope);

        Result<FederationInboundOutcome> result = await h.Sut.ReceiveInboundAsync(
            h.PeerId,
            envelope,
            signature
        );

        result.ErrorCode.Should().Be("no_opt_in");
        h.Journal.Appended.Should().BeEmpty();
        h.Translator.Calls.Should().Be(0);
        h.Bus.Published.OfType<FederatedEventRejectedEvent>()
            .Should()
            .ContainSingle(e => e.Reason == "no_opt_in");
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSigningKeyProvider(string keyId, string pem)
        : IFederationSigningKeyProvider
    {
        public Result<FederationSigningKey> GetActiveSigningKey() =>
            Result.Success(new FederationSigningKey(keyId, pem));
    }

    private sealed class FakeTranslator(Result<int> result) : IFederationInboundTranslator
    {
        public int Calls { get; private set; }

        public Task<Result<int>> TranslateAndApplyAsync(
            Guid peerId,
            FederationEventEnvelope envelope,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeHandler : IFederationInboundHandler
    {
        public string Type => BanType;

        public string GatingOptInType => FederationOptInType.SharedChatBans;

        public Task<Result> ApplyAsync(
            Guid peerId,
            Guid targetBroadcasterId,
            FederationEventEnvelope envelope,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException(
                "The gateway routes through the translator, not the handler."
            );
    }

    private sealed class FakeEventJournal : IEventJournal
    {
        private long _position;
        private readonly HashSet<Guid> _seen = [];

        public List<AppendEventRequest> Appended { get; } = [];

        public void Seed(Guid eventId) => _seen.Add(eventId);

        public Task<Result<EventRecord>> AppendAsync(
            AppendEventRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Appended.Add(request);
            _seen.Add(request.EventId);
            long position = ++_position;
            return Task.FromResult(
                Result.Success(
                    new EventRecord(
                        position,
                        request.EventId,
                        request.BroadcasterId,
                        position,
                        request.EventType,
                        request.EventVersion,
                        request.Source,
                        request.PayloadJson,
                        false,
                        null,
                        request.CorrelationId,
                        request.CausationId,
                        request.ActorUserId,
                        request.ActorExternalUserId,
                        request.ActorProvider,
                        request.MetadataJson,
                        request.OccurredAt,
                        request.OccurredAt
                    )
                )
            );
        }

        public Task<Result<EventRecord>> GetByEventIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                _seen.Contains(eventId)
                    ? Result.Success(
                        new EventRecord(
                            1,
                            eventId,
                            null,
                            1,
                            BanType,
                            1,
                            "federation",
                            "{}",
                            false,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            "{}",
                            Now.UtcDateTime,
                            Now.UtcDateTime
                        )
                    )
                    : Result.Failure<EventRecord>("Not found.", "NOT_FOUND")
            );

        public Task<Result<IReadOnlyList<EventRecord>>> AppendBatchAsync(
            IReadOnlyList<AppendEventRequest> requests,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<EventRecord>>> ReadStreamAsync(
            Guid? broadcasterId,
            long afterPosition,
            int limit,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<EventRecord>>> ReadAllAsync(
            long afterId,
            int limit,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlySet<Guid>>> GetExistingEventIdsAsync(
            IReadOnlyCollection<Guid> candidateEventIds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<long>> GetHeadPositionAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<PagedList<EventRecord>>> QueryAsync(
            EventJournalQuery query,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
