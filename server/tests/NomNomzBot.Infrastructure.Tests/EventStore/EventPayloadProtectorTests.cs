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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// The journal payload-encryption WRITE seam (gdpr-crypto.md §3.4), end-to-end over the REAL crypto stack:
/// AES-256-GCM field cipher + OS-store key vault + the persisted DEK registry. Proves a PII-bearing event
/// (attributed to an internal subject) is journaled as ciphertext under the subject's DEK, that the read side
/// round-trips it back to the original plaintext, that a subject-less event stays plaintext, and — the whole
/// point — that the DEK sealing the row is the very key the erasure pipeline resolves and crypto-shreds, after
/// which the row is permanently unreadable.
/// </summary>
public sealed class EventPayloadProtectorTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    // A chat-message-shaped payload carrying a viewer's PII (display name + message text).
    private const string PiiPayload =
        """{"userId":"55123","displayName":"Alice","message":"hey chat how are we doing"}""";

    private static AppendEventRequest Request(
        Guid? broadcasterId,
        Guid? actorUserId,
        string payload = PiiPayload,
        string eventType = "channel.chat.message"
    ) =>
        new(
            EventId: Guid.NewGuid(),
            BroadcasterId: broadcasterId,
            EventType: eventType,
            EventVersion: 1,
            Source: "eventsub",
            PayloadJson: payload,
            MetadataJson: "{}",
            OccurredAt: new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc),
            ActorUserId: actorUserId
        );

    private sealed record Harness(
        EventJournalService Journal,
        IEventPayloadProtector Protector,
        ISubjectKeyService SubjectKeys
    );

    // The journal runs on real SQLite (its own event-store context); the DEK registry the protector composes runs
    // on the in-memory auth context — exactly the two seams that meet in production, wired to their real impls.
    private static Harness Build(EventStoreTestDbContext eventDb)
    {
        AuthDbContext authDb = AuthTestBuilder.NewContext();
        AuthTestBuilder.RealTokenProtector(authDb, out ISubjectKeyService subjectKeys);
        EventPayloadProtector protector = new(subjectKeys);
        EventJournalService journal = new(
            eventDb,
            new TenantSequenceAllocator(eventDb),
            new EventStoreTestUnitOfWork(eventDb),
            Clock,
            protector
        );
        return new Harness(journal, protector, subjectKeys);
    }

    [Fact]
    public async Task PiiEvent_IsJournaledEncrypted_AndTheReadPathRoundTripsToPlaintext()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext eventDb = database.NewContext();
        Harness harness = Build(eventDb);
        Guid actor = Guid.CreateVersion7();
        AppendEventRequest request = Request(broadcasterId: Guid.NewGuid(), actorUserId: actor);

        Result<EventRecord> appended = await harness.Journal.AppendAsync(request);

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        EventRecord row = appended.Value;

        // The row is sealed: flagged encrypted, tagged with a subject DEK, and the stored payload is ciphertext —
        // it is neither the plaintext nor does it leak the plaintext's contents.
        row.PayloadIsEncrypted.Should().BeTrue();
        row.SubjectKeyId.Should().NotBeNull().And.NotBe(Guid.Empty);
        row.PayloadJson.Should().NotBe(PiiPayload);
        row.PayloadJson.Should().NotContain("Alice");
        row.PayloadJson.Should().NotContain("hey chat");

        // The persisted row (re-read from storage, not the in-memory return) round-trips to the exact original.
        Result<EventRecord> reread = await harness.Journal.GetByEventIdAsync(request.EventId);
        reread.IsSuccess.Should().BeTrue();
        reread.Value.PayloadIsEncrypted.Should().BeTrue();

        Result<string> opened = await harness.Protector.UnprotectAsync(reread.Value);
        opened.IsSuccess.Should().BeTrue(opened.ErrorMessage);
        opened.Value.Should().Be(PiiPayload);
    }

    [Fact]
    public async Task SubjectLessEvent_StaysPlaintext()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext eventDb = database.NewContext();
        Harness harness = Build(eventDb);
        const string payload = """{"category":"Just Chatting","title":"stream up"}""";
        AppendEventRequest request = Request(
            broadcasterId: Guid.NewGuid(),
            actorUserId: null, // no subject attribution → nothing to seal against
            payload: payload,
            eventType: "stream.online"
        );

        Result<EventRecord> appended = await harness.Journal.AppendAsync(request);

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        appended.Value.PayloadIsEncrypted.Should().BeFalse();
        appended.Value.SubjectKeyId.Should().BeNull();
        appended.Value.PayloadJson.Should().Be(payload);

        // The plaintext read path returns the payload verbatim (no decrypt attempted).
        Result<string> opened = await harness.Protector.UnprotectAsync(appended.Value);
        opened.IsSuccess.Should().BeTrue();
        opened.Value.Should().Be(payload);
    }

    [Fact]
    public async Task JournalRowKey_IsInTheErasureShredSet_AndShreddingItMakesTheRowUnreadable()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext eventDb = database.NewContext();
        Harness harness = Build(eventDb);
        Guid actor = Guid.CreateVersion7();
        AppendEventRequest request = Request(broadcasterId: Guid.NewGuid(), actorUserId: actor);

        EventRecord row = (await harness.Journal.AppendAsync(request)).Value;
        Guid journalKeyId = row.SubjectKeyId!.Value;

        // The erasure planner resolves the subject's shred set from the SAME identity derivation the seam used
        // (ConsentService.ComputeSubjectIdHash of the internal user id) — so the journal's DEK is in that set.
        string subjectIdHash = ConsentService.ComputeSubjectIdHash(actor);
        Result<IReadOnlyList<Guid>> shredSet = await harness.SubjectKeys.ResolveSubjectKeysAsync(
            actor,
            subjectIdHash
        );
        shredSet.IsSuccess.Should().BeTrue(shredSet.ErrorMessage);
        shredSet
            .Value.Should()
            .Contain(
                journalKeyId,
                "erasure must reach the DEK sealing the journal row, or the PII survives crypto-shred"
            );

        // Sanity: readable before the shred.
        (await harness.Protector.UnprotectAsync(row))
            .Value.Should()
            .Be(PiiPayload);

        // Crypto-shred the resolved key → the same row can no longer be opened (GDPR guarantee, surfaced as a
        // closed KEY_DESTROYED failure, never silent plaintext).
        (await harness.SubjectKeys.DestroyKeyAsync(journalKeyId, Guid.CreateVersion7()))
            .IsSuccess.Should()
            .BeTrue();

        Result<string> afterShred = await harness.Protector.UnprotectAsync(row);
        afterShred.IsFailure.Should().BeTrue();
        afterShred.ErrorCode.Should().Be("KEY_DESTROYED");
    }

    [Fact]
    public async Task Ciphertext_CannotBeReadUnderADifferentEventType()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext eventDb = database.NewContext();
        Harness harness = Build(eventDb);
        AppendEventRequest request = Request(
            broadcasterId: Guid.NewGuid(),
            actorUserId: Guid.CreateVersion7()
        );

        EventRecord row = (await harness.Journal.AppendAsync(request)).Value;

        // The AAD binds the event type: the identical ciphertext presented as another event type fails the tag
        // check, so a chat-message payload cannot be transplanted onto, say, a subscribe row under the same DEK.
        EventRecord transplanted = row with
        {
            EventType = "channel.subscribe",
        };
        Result<string> opened = await harness.Protector.UnprotectAsync(transplanted);

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("DECRYPT_FAILED");
    }
}
