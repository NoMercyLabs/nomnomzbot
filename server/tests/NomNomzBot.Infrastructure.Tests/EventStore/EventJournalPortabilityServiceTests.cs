// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Behavior tests for portable journal export/import. Each asserts a consequence of the action: that a round-trip
/// into a fresh journal reconstructs IDENTICAL projected state purely from the exported file, that a second import
/// is a true no-op (idempotency on the globally-unique <c>EventId</c>), that an import never disturbs an unrelated
/// tenant and stamps the importing tenant onto every row, and that a stale-shaped exported event is upcast to the
/// current shape before it is persisted. No test asserts merely "it returned" or "did not throw".
/// <para>
/// Source and target live in SEPARATE SQLite databases — the real portability scenario is export-from-one-deployment,
/// import-into-another. (<c>EventId</c> is globally unique in a journal, so re-importing into the SAME database that
/// already holds the source events is, correctly, a no-op; the cross-deployment split is what exercises a real append.)
/// </para>
/// </summary>
public sealed class EventJournalPortabilityServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(db, new TenantSequenceAllocator(db), new EventStoreTestUnitOfWork(db), Clock);

    private static EventJournalPortabilityService NewPortability(
        EventJournalService journal,
        EventUpcasterRegistry upcasters
    ) => new(journal, upcasters);

    private static AppendEventRequest CounterEvent(
        Guid tenant,
        long amount,
        int version,
        string field,
        Guid? eventId = null
    ) =>
        new(
            EventId: eventId ?? Guid.NewGuid(),
            BroadcasterId: tenant,
            EventType: "counter.incremented",
            EventVersion: version,
            Source: "domain",
            PayloadJson: $"{{\"key\":\"hits\",\"{field}\":{amount}}}",
            MetadataJson: "{}",
            OccurredAt: new DateTime(2026, 6, 24, 11, 0, 0, DateTimeKind.Utc)
        );

    private static async Task<byte[]> ExportToBytesAsync(
        EventJournalPortabilityService portability,
        Guid tenant
    )
    {
        using MemoryStream destination = new();
        Result<long> export = await portability.ExportAsync(tenant, destination);
        export.IsSuccess.Should().BeTrue(export.ErrorMessage);
        return destination.ToArray();
    }

    [Fact]
    public async Task RoundTrip_ExportIntoFreshJournal_ReconstructsIdenticalProjectedState()
    {
        // ── Source deployment: seed three events, fold the projection — this is the baseline to reproduce. ──
        using SqliteTestDatabase sourceDb = SqliteTestDatabase.Open();
        Guid source = Guid.NewGuid();
        byte[] exported;
        long baseline;

        await using (EventStoreTestDbContext db = sourceDb.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            EventUpcasterRegistry upcasters = new([]);
            await journal.AppendAsync(CounterEvent(source, 5, version: 2, field: "amount"));
            await journal.AppendAsync(CounterEvent(source, 7, version: 2, field: "amount"));
            await journal.AppendAsync(CounterEvent(source, 3, version: 2, field: "amount"));

            CounterProjection sourceProjection = new();
            ProjectionRunner sourceRunner = new([sourceProjection], journal, upcasters, db, Clock);
            await sourceRunner.RunOnceAsync(CounterProjection.ProjectionName, source);
            baseline = sourceProjection.State["hits"];
            baseline.Should().Be(15);

            exported = await ExportToBytesAsync(NewPortability(journal, upcasters), source);
            new string(Encoding.UTF8.GetChars(exported))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Should()
                .HaveCount(3, "one JSONL line per exported event");
        }

        // ── Target deployment: a fresh, empty journal. Import the file under a DIFFERENT tenant id. ──
        using SqliteTestDatabase targetDb = SqliteTestDatabase.Open();
        Guid target = Guid.NewGuid();

        await using EventStoreTestDbContext targetContext = targetDb.NewContext();
        EventJournalService targetJournal = NewJournal(targetContext);
        EventUpcasterRegistry targetUpcasters = new([]);

        using MemoryStream importStream = new(exported);
        Result<EventJournalImportSummary> import = await NewPortability(
                targetJournal,
                targetUpcasters
            )
            .ImportAsync(target, importStream);

        import.IsSuccess.Should().BeTrue(import.ErrorMessage);
        import.Value.TotalLines.Should().Be(3);
        import
            .Value.Imported.Should()
            .Be(3, "all three events are new in the fresh target journal");
        import.Value.SkippedDuplicate.Should().Be(0);

        // Fold the target purely from the imported journal — its state must equal the source baseline.
        CounterProjection targetProjection = new();
        ProjectionRunner targetRunner = new(
            [targetProjection],
            targetJournal,
            targetUpcasters,
            targetContext,
            Clock
        );
        Result<long> applied = await targetRunner.RunOnceAsync(
            CounterProjection.ProjectionName,
            target
        );

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(3);
        targetProjection
            .State["hits"]
            .Should()
            .Be(
                baseline,
                "import reconstructs identical projected state from the exported journal alone"
            );
    }

    [Fact]
    public async Task ReImport_SecondPass_SkipsEveryEvent_NoDuplicates()
    {
        // Export from a source deployment.
        using SqliteTestDatabase sourceDb = SqliteTestDatabase.Open();
        Guid source = Guid.NewGuid();
        byte[] exported;
        await using (EventStoreTestDbContext db = sourceDb.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            await journal.AppendAsync(CounterEvent(source, 5, version: 2, field: "amount"));
            await journal.AppendAsync(CounterEvent(source, 7, version: 2, field: "amount"));
            exported = await ExportToBytesAsync(NewPortability(journal, new([])), source);
        }

        using SqliteTestDatabase targetDb = SqliteTestDatabase.Open();
        Guid target = Guid.NewGuid();
        await using EventStoreTestDbContext targetContext = targetDb.NewContext();
        EventJournalService targetJournal = NewJournal(targetContext);
        EventJournalPortabilityService portability = NewPortability(targetJournal, new([]));

        using (MemoryStream first = new(exported))
        {
            Result<EventJournalImportSummary> firstImport = await portability.ImportAsync(
                target,
                first
            );
            firstImport.IsSuccess.Should().BeTrue(firstImport.ErrorMessage);
            firstImport.Value.Imported.Should().Be(2);
            firstImport.Value.SkippedDuplicate.Should().Be(0);
        }

        long headAfterFirst = (await targetJournal.GetHeadPositionAsync(target)).Value;
        headAfterFirst.Should().Be(2);

        // Second import of the same file: every event already exists → all skipped, zero new positions consumed.
        using MemoryStream second = new(exported);
        Result<EventJournalImportSummary> secondImport = await portability.ImportAsync(
            target,
            second
        );

        secondImport.IsSuccess.Should().BeTrue(secondImport.ErrorMessage);
        secondImport.Value.TotalLines.Should().Be(2);
        secondImport
            .Value.Imported.Should()
            .Be(0, "a re-import of existing events imports nothing");
        secondImport.Value.SkippedDuplicate.Should().Be(2, "both events were already present");

        long headAfterSecond = (await targetJournal.GetHeadPositionAsync(target)).Value;
        headAfterSecond
            .Should()
            .Be(headAfterFirst, "idempotent re-import advances the head by zero");

        Result<IReadOnlyList<EventRecord>> stream = await targetJournal.ReadStreamAsync(
            target,
            0,
            100
        );
        stream.Value.Should().HaveCount(2, "no duplicate rows were appended");
        stream
            .Value.Select(e => e.EventId)
            .Should()
            .OnlyHaveUniqueItems("every EventId in the target stream is distinct");
    }

    [Fact]
    public async Task Import_NeverDisturbsOtherTenant_AndStampsImportingTenant()
    {
        // Export from a source deployment (the file's lines carry the source's BroadcasterId).
        using SqliteTestDatabase sourceDb = SqliteTestDatabase.Open();
        Guid source = Guid.NewGuid();
        byte[] exported;
        await using (EventStoreTestDbContext db = sourceDb.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            await journal.AppendAsync(CounterEvent(source, 4, version: 2, field: "amount"));
            await journal.AppendAsync(CounterEvent(source, 8, version: 2, field: "amount"));
            exported = await ExportToBytesAsync(NewPortability(journal, new([])), source);
        }

        // Target deployment already hosts an unrelated bystander tenant with its own event.
        using SqliteTestDatabase targetDb = SqliteTestDatabase.Open();
        Guid bystander = Guid.NewGuid();
        Guid target = Guid.NewGuid();
        await using EventStoreTestDbContext targetContext = targetDb.NewContext();
        EventJournalService targetJournal = NewJournal(targetContext);
        await targetJournal.AppendAsync(CounterEvent(bystander, 99, version: 2, field: "amount"));
        long bystanderHeadBefore = (await targetJournal.GetHeadPositionAsync(bystander)).Value;

        using MemoryStream importStream = new(exported);
        Result<EventJournalImportSummary> import = await NewPortability(targetJournal, new([]))
            .ImportAsync(target, importStream);
        import.IsSuccess.Should().BeTrue(import.ErrorMessage);
        import.Value.Imported.Should().Be(2);

        // Every imported row landed under the TARGET tenant — not the source tenant the file named. The wall is
        // the re-tenanting on import.
        Result<IReadOnlyList<EventRecord>> targetStream = await targetJournal.ReadStreamAsync(
            target,
            0,
            100
        );
        targetStream.Value.Should().HaveCount(2);
        targetStream
            .Value.Should()
            .OnlyContain(
                e => e.BroadcasterId == target,
                "imported rows are stamped with the importing tenant, never the file's source tenant"
            );
        targetStream
            .Value.Should()
            .NotContain(
                e => e.BroadcasterId == source,
                "the source tenant id from the file is never written into the journal"
            );

        // The bystander tenant is completely unaffected — same head, only its own row.
        long bystanderHeadAfter = (await targetJournal.GetHeadPositionAsync(bystander)).Value;
        bystanderHeadAfter
            .Should()
            .Be(bystanderHeadBefore, "an unrelated tenant's stream never moves during an import");
        Result<IReadOnlyList<EventRecord>> bystanderStream = await targetJournal.ReadStreamAsync(
            bystander,
            0,
            100
        );
        bystanderStream.Value.Should().HaveCount(1);
        bystanderStream
            .Value.Should()
            .OnlyContain(
                e => e.BroadcasterId == bystander,
                "no imported event bled into the bystander"
            );
    }

    [Fact]
    public async Task Import_UpcastsStaleEventShape_BeforeItIsPersisted()
    {
        // Export a stale v1 row (increment under "value") alongside a current v2 row ("amount").
        using SqliteTestDatabase sourceDb = SqliteTestDatabase.Open();
        Guid source = Guid.NewGuid();
        byte[] exported;
        await using (EventStoreTestDbContext db = sourceDb.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            // No upcaster on the source — the v1 row is exported verbatim, stale shape and all.
            await journal.AppendAsync(CounterEvent(source, 4, version: 1, field: "value"));
            await journal.AppendAsync(CounterEvent(source, 6, version: 2, field: "amount"));
            exported = await ExportToBytesAsync(NewPortability(journal, new([])), source);
        }

        // The TARGET deployment knows the v1->v2 upcaster. The projection only understands "amount", so without
        // an upcast-on-import the v1 event would fold to 0.
        using SqliteTestDatabase targetDb = SqliteTestDatabase.Open();
        Guid target = Guid.NewGuid();
        await using EventStoreTestDbContext targetContext = targetDb.NewContext();
        EventJournalService targetJournal = NewJournal(targetContext);
        EventUpcasterRegistry upcasters = new([new CounterV1ToV2Upcaster()]);

        using MemoryStream importStream = new(exported);
        Result<EventJournalImportSummary> import = await NewPortability(targetJournal, upcasters)
            .ImportAsync(target, importStream);

        import.IsSuccess.Should().BeTrue(import.ErrorMessage);
        import.Value.Upcast.Should().Be(1, "exactly the one v1 row was upcast to v2 on import");

        // The persisted row carries the current version and the upcast payload shape.
        Result<IReadOnlyList<EventRecord>> targetStream = await targetJournal.ReadStreamAsync(
            target,
            0,
            100
        );
        targetStream.Value.Should().HaveCount(2);
        targetStream
            .Value.Should()
            .OnlyContain(
                e => e.EventVersion == 2,
                "imported rows are stamped at the current event version"
            );
        targetStream
            .Value.Should()
            .OnlyContain(
                e => e.PayloadJson.Contains("amount") && !e.PayloadJson.Contains("value"),
                "the upcaster renamed value->amount before the row was persisted"
            );

        // Folding the target proves the consequence: the v1 increment (4) was upcast and summed with the v2 (6).
        CounterProjection projection = new();
        ProjectionRunner runner = new([projection], targetJournal, upcasters, targetContext, Clock);
        await runner.RunOnceAsync(CounterProjection.ProjectionName, target);
        projection
            .State["hits"]
            .Should()
            .Be(
                10,
                "the upcast v1 row (4) and the v2 row (6) both folded into the target projection"
            );
    }
}
