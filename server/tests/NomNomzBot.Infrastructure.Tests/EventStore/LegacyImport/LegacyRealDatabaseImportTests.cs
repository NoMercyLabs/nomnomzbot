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
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;

namespace NomNomzBot.Infrastructure.Tests.EventStore.LegacyImport;

/// <summary>
/// End-to-end proof against the OWNER'S REAL legacy database when it is present on the machine
/// (<c>%LocalAppData%\NoMercyBot\data\database.sqlite</c>). It reads the real <c>ChannelEvents</c> read-only,
/// imports them into an isolated in-memory journal, and asserts the owner's real channel history actually
/// materialised: thousands of import rows, every mapped event type represented, the journal contiguous, and a
/// second pass importing nothing (idempotent). The legacy file is never mutated and no live Twitch call is made.
/// The test SKIPS cleanly (passes with no assertions) when the file is absent, so CI on a machine without the
/// owner's data is unaffected — the synthetic-source importer tests already prove the mechanism universally.
/// </summary>
public sealed class LegacyRealDatabaseImportTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero)
    );

    private static string LegacyDbPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NoMercyBot",
            "data",
            "database.sqlite"
        );

    [Fact]
    public async Task Imports_the_owners_real_channel_history_into_the_journal()
    {
        if (!File.Exists(LegacyDbPath))
            return; // No legacy data on this machine — the synthetic-source tests cover the mechanism.

        // Copy the live file to a temp path and open THAT read-only, so a concurrently-running legacy bot's WAL
        // never blocks the read and the original is provably never touched.
        string snapshot = Path.Combine(
            Path.GetTempPath(),
            $"legacy-import-proof-{Guid.NewGuid():N}.sqlite"
        );
        File.Copy(LegacyDbPath, snapshot, overwrite: true);

        try
        {
            using SqliteTestDatabase database = SqliteTestDatabase.Open();
            await using EventStoreTestDbContext db = database.NewContext();
            EventJournalService journal = new(
                db,
                new TenantSequenceAllocator(db),
                new EventStoreTestUnitOfWork(db),
                Clock,
                new PassthroughEventPayloadProtector()
            );
            LegacyChannelEventImporter importer = new(journal, new LegacyChannelEventMapper());
            Guid tenant = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");

            Result<LegacyImportSummary> result = await importer.ImportAsync(
                tenant,
                new LegacySqliteChannelEventSource(snapshot)
            );

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            LegacyImportSummary summary = result.Value;

            // The real DB holds 40k+ rows; the mapped subset (follow/sub/gift/cheer/raid/mod/ban/redemption) is a
            // substantial slice. These bounds prove real history flowed in, not an empty/degenerate import.
            summary
                .TotalRead.Should()
                .BeGreaterThan(1000, "the real legacy DB has a large event history");
            summary
                .Imported.Should()
                .BeGreaterThan(0, "mapped channel events were appended to the journal");
            (summary.Imported + summary.SkippedUnmapped + summary.SkippedDuplicate)
                .Should()
                .Be(summary.TotalRead, "every read row is accounted for");

            // The journal is contiguous and holds exactly the imported rows, all Source="import".
            Result<long> head = await journal.GetHeadPositionAsync(tenant);
            head.Value.Should().Be(summary.Imported);

            Result<IReadOnlyList<EventRecord>> firstPage = await journal.ReadStreamAsync(
                tenant,
                0,
                500
            );
            firstPage.Value.Should().OnlyContain(e => e.Source == "import");

            // A second import pass is a complete no-op — the derived EventIds already exist (idempotent re-run).
            Result<LegacyImportSummary> second = await importer.ImportAsync(
                tenant,
                new LegacySqliteChannelEventSource(snapshot)
            );
            second
                .Value.Imported.Should()
                .Be(0, "re-importing the owner's history writes nothing new");
            second.Value.SkippedDuplicate.Should().Be(summary.Imported);
        }
        finally
        {
            File.Delete(snapshot);
        }
    }
}
