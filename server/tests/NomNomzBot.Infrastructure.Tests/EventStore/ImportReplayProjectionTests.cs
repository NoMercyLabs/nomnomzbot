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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Proves the 2026-08-27 incident fix: "replay" is a permanently silent no-op. It used to re-publish every
/// imported event onto the live event bus, and because <see cref="EventStoreProjectionDriver"/> drives every
/// registered projection for every channel forever, that turned a single rebuild into a live side-effect
/// cannon — real Spotify calls, TTS utterances, and Helix redemption updates re-fired across 17 broadcasters
/// on a schedule nobody could see or stop. Owner directive: a replay must never touch any outside system
/// again. <see cref="ImportReplayProjection"/> no longer even holds an event bus reference — publishing is
/// structurally impossible, not merely skipped — so these tests only need to prove the checkpoint bookkeeping
/// (advance, safe re-run, tenant scoping, tolerate garbage) still behaves correctly.
/// </summary>
public sealed class ImportReplayProjectionTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>()
        );

    private static ProjectionRunner NewRunner(
        EventStoreTestDbContext db,
        EventJournalService journal,
        ImportReplayProjection projection
    ) =>
        new(
            [projection],
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock,
            new NoOpRunOnceGuard()
        );

    private static AppendEventRequest ImportedFollow(Guid tenant, string userId)
    {
        Guid eventId = Guid.NewGuid();
        return new(
            EventId: eventId,
            BroadcasterId: tenant,
            EventType: nameof(FollowEvent),
            EventVersion: 1,
            Source: "import",
            PayloadJson: JsonPayload(eventId, tenant, userId),
            MetadataJson: "{}",
            OccurredAt: new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        );
    }

    private static string JsonPayload(Guid eventId, Guid tenant, string userId) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(
            new FollowEvent
            {
                EventId = eventId,
                BroadcasterId = tenant,
                OccurredAt = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                UserId = userId,
                UserDisplayName = "Viewer " + userId,
                UserLogin = "viewer" + userId,
                FollowedAt = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );

    [Fact]
    public async Task RunOnce_ImportedEvent_AdvancesTheCheckpoint()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-42"));

        ImportReplayProjection projection = new();
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_AnUnrecognizedOrMalformedEvent_StillAdvancesWithoutFailingTheRun()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(
            ImportedFollow(tenant, "viewer-1") with
            {
                EventId = Guid.NewGuid(),
                EventType = "SomeEventTypeThatWasRemovedOrNeverExisted",
            }
        );
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-2"));

        ImportReplayProjection projection = new();
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(2);
    }

    [Fact]
    public async Task RunOnce_CalledTwiceWithNothingNewImported_IsASafeNoOpTheSecondTime()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-1"));

        ImportReplayProjection projection = new();
        ProjectionRunner runner = NewRunner(db, journal, projection);

        await runner.RunOnceAsync(projection.Name, tenant);

        Result<long> second = await runner.RunOnceAsync(projection.Name, tenant);

        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        second.Value.Should().Be(0); // checkpoint already at head — nothing new to apply
    }

    [Fact]
    public async Task RunOnce_ScopedToOneTenant_NeverTouchesAnotherTenantsCheckpoint()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenantA, "a-viewer"));
        await journal.AppendAsync(ImportedFollow(tenantB, "b-viewer"));

        ImportReplayProjection projection = new();
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenantA);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(1); // only tenant A's own imported row, never tenant B's

        Result<ProjectionCheckpointDto> checkpointB = await runner.GetCheckpointAsync(
            projection.Name,
            tenantB
        );
        checkpointB.IsFailure.Should().BeTrue(); // tenant B was never touched — no checkpoint exists for it
    }
}
