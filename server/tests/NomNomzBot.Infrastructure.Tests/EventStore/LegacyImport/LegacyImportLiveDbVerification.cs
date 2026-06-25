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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.EventStore.LegacyImport;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore.LegacyImport;

// TEMPORARY verification harness — runs the REAL LegacyChannelImportService + ProjectionRunner against a COPY of the
// owner's live nomnomz.db (read+write on the copy; the original is never touched) and the READ-ONLY legacy SQLite.
// Proves the full backfill loop end-to-end with real numbers, then is deleted. Skips when the live DBs are absent.
public sealed class LegacyImportLiveDbVerification
{
    private static readonly Guid Tenant = Guid.Parse("019EF8E4-EAA1-736F-B992-7778DC68F241");

    private static string LiveDb =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NomNomzBot",
            "nomnomz.db"
        );

    private static string LegacyDb =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NoMercyBot",
            "data",
            "database.sqlite"
        );

    [Fact]
    public async Task Imports_all_legacy_events_with_real_ids_and_links_viewers()
    {
        if (!File.Exists(LiveDb) || !File.Exists(LegacyDb))
            return; // CI without the owner's data — nothing to verify here.

        string work = Path.Combine(Path.GetTempPath(), $"nomnomz-verify-{Guid.NewGuid():N}.db");
        CopyDatabase(LiveDb, work);
        Environment.SetEnvironmentVariable("NOMNOMZ_LEGACY_DB", LegacyDb);

        try
        {
            // ── before ──
            long journalBefore;
            long channelEventsBefore;
            await using (AppDbContext db = OpenLive(work))
            {
                journalBefore = await db.EventJournals.LongCountAsync();
                channelEventsBefore = await db.ChannelEvents.LongCountAsync();
            }

            // ── import ──
            LegacyImportResult import;
            await using (AppDbContext db = OpenLive(work))
            {
                LegacyChannelImportService service = NewService(db);
                Result<LegacyImportResult> result = await service.ImportLegacyAsync(Tenant);
                result.IsSuccess.Should().BeTrue(result.ErrorMessage);
                import = result.Value;
            }

            // Every legacy row is accounted for: imported + duplicate + itemized skips = total read (no silent drop).
            (import.Imported + import.SkippedDuplicate + import.SkippedUnmapped)
                .Should()
                .Be(import.TotalRead);
            import.SkippedUnmapped.Should().Be(import.SkippedByLegacyType.Values.Sum());
            import.TotalRead.Should().BeGreaterThan(41000, "the legacy history is ~41.5k rows");
            // The mappable bulk lands on the journal — whether THIS run appended it (a clean live DB) or it was
            // already present (the live DB the standalone CLI has since imported into, copied here). Idempotency makes
            // the end-state identical, so assert the mapped total via imported+duplicate, not freshly-imported alone.
            // The legacy ~41.5k rows map to ~33.2k distinct journal events (chat keyed on the real Twitch MessageId
            // dedupes repeats, redemptions on the real redemption GUID, and ~8.3k noise/CRUD rows are unmapped).
            (import.Imported + import.SkippedDuplicate)
                .Should()
                .BeGreaterThan(32000, "the chat bulk (~29k) is mapped onto the journal");

            // ── rebuild ──
            await using (AppDbContext db = OpenLive(work))
            {
                LegacyChannelImportService service = NewService(db);
                Result<IReadOnlyList<ProjectionRebuildResult>> rebuild =
                    await service.RebuildProjectionsAsync(Tenant);
                rebuild.IsSuccess.Should().BeTrue(rebuild.ErrorMessage);
            }

            // ── verify ──
            await using (AppDbContext db = OpenLive(work))
            {
                long journalAfter = await db.EventJournals.LongCountAsync();
                long channelEvents = await db.ChannelEvents.LongCountAsync();
                long withUser = await db.ChannelEvents.LongCountAsync(e => e.UserId != null);
                int distinctViewers = await db
                    .ChannelEvents.Where(e => e.UserId != null)
                    .Select(e => e.UserId)
                    .Distinct()
                    .CountAsync();
                int chatRows = await db.ChannelEvents.CountAsync(e =>
                    e.Type == "channel.chat.message"
                );

                // The legacy ~41.5k rows map to ~33.2k distinct journal events (chat keyed on the real Twitch
                // MessageId dedupes repeats, redemptions on the real redemption GUID, and ~8.3k noise/CRUD rows are
                // unmapped), so the channel-event log spans ~33.2k facts — not the raw 41.5k. Assert that real,
                // idempotent end-state.
                journalAfter
                    .Should()
                    .BeGreaterThan(32000, "the mappable bulk is on the journal");
                channelEvents
                    .Should()
                    .BeGreaterThan(32000, "the channel-event log spans the mapped ~33.2k facts");
                withUser
                    .Should()
                    .BeGreaterThan(32000, "every attributable fact links to a User surrogate");
                distinctViewers.Should().BeGreaterThan(0, "distinct viewers must be > 0");
                chatRows.Should().BeGreaterThan(27000, "the chat-message rows are present");

                // Real Twitch id sample: a chat row's id is its real Twitch MessageId GUID (carried in Data too).
                ChannelEventSample sample = await db
                    .ChannelEvents.Where(e => e.Type == "channel.chat.message")
                    .OrderBy(e => e.Id)
                    .Select(e => new ChannelEventSample(e.Id, e.UserId, e.Data))
                    .FirstAsync();
                Guid.TryParse(sample.Id, out _)
                    .Should()
                    .BeTrue("the channel-event id IS the real Twitch message GUID");
                sample.UserId.Should().NotBeNull("the chat row links to the chatter's User");

                // The historic event time survives in Data.occurredAt even though the audit interceptor clobbered
                // ChannelEvent.CreatedAt with the import time — proving occurredAt preservation.
                Newtonsoft.Json.Linq.JObject parsedData = Newtonsoft.Json.Linq.JObject.Parse(
                    sample.Data!
                );
                Newtonsoft.Json.Linq.JToken? occurredAt = parsedData["occurredAt"];
                occurredAt
                    .Should()
                    .NotBeNull("the historic event time is carried in Data.occurredAt");
                DateTime parsedOccurred = occurredAt!.ToObject<DateTime>().ToUniversalTime();
                // The legacy history runs into 2026, so an arbitrary calendar cutoff is wrong. The real invariant is
                // that occurredAt is the historic moment, NOT the import write time the audit interceptor stamps onto
                // CreatedAt — the FakeTimeProvider pins that to 2026-06-24 12:00Z, so assert occurredAt differs from it.
                DateTime importWriteTime = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
                parsedOccurred
                    .Should()
                    .NotBe(
                        importWriteTime,
                        "occurredAt carries the historic event time, not the import write time"
                    );
                parsedOccurred
                    .Should()
                    .BeBefore(
                        importWriteTime,
                        "every imported fact occurred before this import ran"
                    );

                // ── idempotency: a second import appends nothing ──
                LegacyChannelImportService service = NewService(db);
                Result<LegacyImportResult> rerun = await service.ImportLegacyAsync(Tenant);
                rerun.IsSuccess.Should().BeTrue(rerun.ErrorMessage);
                rerun.Value.Imported.Should().Be(0, "a re-run is a no-op on the real EventId");
                rerun.Value.SkippedDuplicate.Should().Be(import.Imported + import.SkippedDuplicate);
                (await db.EventJournals.LongCountAsync())
                    .Should()
                    .Be(journalAfter, "no new journal rows on re-import");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOMNOMZ_LEGACY_DB", null);
            TryDeleteDatabase(work);
        }
    }

    private sealed record ChannelEventSample(string Id, Guid? UserId, string? Data);

    private static LegacyChannelImportService NewService(AppDbContext db)
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero));
        EventJournalService journal = new(
            db,
            new TenantSequenceAllocator(db),
            new LiveDbUnitOfWork(db),
            clock
        );

        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        IUserService userService = new UserService(
            db,
            currentUser,
            Substitute.For<IServiceScopeFactory>()
        );
        ViewerResolver resolver = new(db, userService);
        ILiveWindowResolver liveWindow = Substitute.For<ILiveWindowResolver>();
        liveWindow
            .GetCoveringStreamIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("stream-import");

        List<IProjection> projections =
        [
            new TwitchChannelEventLogProjection(db),
            new ChannelAnalyticsDailyProjection(db),
            new MessageActivityDailyProjection(db, resolver),
            new ViewerEngagementDailyProjection(db, resolver),
            new ViewerProfileProjection(db, resolver),
            new WatchSessionProjection(db, resolver, liveWindow),
        ];

        ProjectionRunner runner = new(
            projections,
            journal,
            new EventUpcasterRegistry([]),
            db,
            clock
        );

        return new LegacyChannelImportService(
            journal,
            runner,
            projections,
            new DefaultLegacyDatabaseLocator(),
            new ChannelEventActorBackfill(db),
            NullLogger<LegacyChannelImportService>.Instance
        );
    }

    // A real AppDbContext on the working copy, with no tenant filter (so the verification sees every row) and the
    // SAME audit + soft-delete interceptors production attaches — so the AuditableEntityInterceptor clobbers
    // ChannelEvent.CreatedAt with the import time exactly as in production, proving the historic event time survives
    // only because the projection carries it in Data.occurredAt.
    private static AppDbContext OpenLive(string path)
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero));
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .AddInterceptors(
                new NomNomzBot.Infrastructure.Platform.Persistence.Interceptors.AuditableEntityInterceptor(
                    clock
                ),
                new NomNomzBot.Infrastructure.Platform.Persistence.Interceptors.SoftDeleteInterceptor(
                    clock
                )
            )
            .Options;
        return new AppDbContext(options);
    }

    private static void CopyDatabase(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        foreach (string suffix in new[] { "-wal", "-shm" })
            if (File.Exists(source + suffix))
                File.Copy(source + suffix, destination + suffix, overwrite: true);
    }

    private static void TryDeleteDatabase(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string p in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(p))
            {
                try
                {
                    File.Delete(p);
                }
                catch (IOException) { }
            }
    }
}

internal sealed class LiveDbUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public LiveDbUnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default) =>
        _transaction = await _db.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
