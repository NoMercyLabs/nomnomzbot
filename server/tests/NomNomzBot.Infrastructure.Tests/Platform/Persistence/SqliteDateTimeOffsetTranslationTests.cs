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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using StreamEntity = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S004e — SQLite (the self_host_lite default runtime) has no native comparison for the default TEXT mapping of
/// <see cref="DateTimeOffset"/> columns, so EF Core cannot translate relational predicates, ORDER BY, MIN/MAX, or
/// GROUP BY keys over them; the query either throws "could not be translated" or silently falls back to
/// client evaluation of the whole table. This harness runs the two named production symptoms — the scheduled-
/// pipeline due-sweep (<c>ScheduledPipelineService.FireDueAsync</c>'s <c>t.DueAt &lt;= now</c>) and the channel
/// analytics stream list's <c>OrderByDescending(s =&gt; s.StartedAt)</c> — against a REAL relational SQLite
/// database (not EF InMemory, which never modeled this at all) through the exact production model-building step
/// (<see cref="ProviderCompatibilityExtensions.ApplySqliteCompatibility"/>) that <c>AppDbContext</c> uses, proving
/// the fix (UTC-ticks value conversion, applied model-wide) makes both operations translate and return correct
/// results — not merely "does not throw".
/// </summary>
public sealed class SqliteDateTimeOffsetTranslationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_dto_translation_{Guid.NewGuid():N}.db"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private async Task<DateTimeOffsetTranslationDbContext> NewContextAsync()
    {
        DbContextOptions<DateTimeOffsetTranslationDbContext> options =
            new DbContextOptionsBuilder<DateTimeOffsetTranslationDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
        DateTimeOffsetTranslationDbContext db = new(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    [Fact]
    public async Task Scheduled_pipeline_due_sweep_returns_only_tasks_whose_DueAt_has_passed()
    {
        DateTimeOffset now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        Guid overdueId = Guid.NewGuid();
        Guid dueNowId = Guid.NewGuid();
        Guid futureId = Guid.NewGuid();

        await using (DateTimeOffsetTranslationDbContext db = await NewContextAsync())
        {
            db.ScheduledPipelineTasks.AddRange(
                NewTask(overdueId, now.AddMinutes(-5)),
                NewTask(dueNowId, now),
                NewTask(futureId, now.AddMinutes(5))
            );
            await db.SaveChangesAsync();
        }

        await using DateTimeOffsetTranslationDbContext readDb = await NewContextAsync();

        // Pre-fix, this predicate over the default TEXT-mapped DateTimeOffset column cannot be translated by the
        // SQLite provider and throws InvalidOperationException at query execution — scheduled pipelines never
        // fire on self-host. Post-fix (UTC-ticks INTEGER column) it translates to a plain integer comparison.
        List<Guid> due = await readDb
            .ScheduledPipelineTasks.Where(t =>
                t.Status == ScheduledPipelineTaskStatus.Pending && t.DueAt <= now
            )
            .Select(t => t.Id)
            .ToListAsync();

        due.Should()
            .BeEquivalentTo(
                [overdueId, dueNowId],
                "only tasks due at or before now are swept as due"
            );
        due.Should().NotContain(futureId, "a future DueAt must never be reported as due");
    }

    [Fact]
    public async Task Scheduled_pipeline_pending_list_orders_correctly_by_DueAt()
    {
        DateTimeOffset baseTime = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        Guid earliest = Guid.NewGuid();
        Guid middle = Guid.NewGuid();
        Guid latest = Guid.NewGuid();

        await using (DateTimeOffsetTranslationDbContext db = await NewContextAsync())
        {
            // Inserted out of chronological order to prove the ORDER BY, not insertion order.
            db.ScheduledPipelineTasks.AddRange(
                NewTask(latest, baseTime.AddHours(2)),
                NewTask(earliest, baseTime),
                NewTask(middle, baseTime.AddHours(1))
            );
            await db.SaveChangesAsync();
        }

        await using DateTimeOffsetTranslationDbContext readDb = await NewContextAsync();
        List<Guid> ordered = await readDb
            .ScheduledPipelineTasks.OrderBy(t => t.DueAt)
            .Select(t => t.Id)
            .ToListAsync();

        ordered.Should().Equal(earliest, middle, latest);
    }

    [Fact]
    public async Task Stream_list_orders_correctly_by_StartedAt_descending()
    {
        Guid channel = Guid.NewGuid();
        DateTimeOffset baseTime = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await using (DateTimeOffsetTranslationDbContext db = await NewContextAsync())
        {
            db.Streams.AddRange(
                NewStream("s-oldest", channel, baseTime),
                NewStream("s-newest", channel, baseTime.AddDays(2)),
                NewStream("s-middle", channel, baseTime.AddDays(1))
            );
            await db.SaveChangesAsync();
        }

        await using DateTimeOffsetTranslationDbContext readDb = await NewContextAsync();

        // Mirrors ChannelAnalyticsService.ListStreamsAsync's query shape exactly.
        List<string> ordered = await readDb
            .Streams.Where(s => s.ChannelId == channel && s.StartedAt != null)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Id)
            .ToListAsync();

        ordered.Should().Equal("s-newest", "s-middle", "s-oldest");
    }

    private static ScheduledPipelineTask NewTask(Guid id, DateTimeOffset dueAt) =>
        new()
        {
            Id = id,
            BroadcasterId = Guid.NewGuid(),
            PipelineId = Guid.NewGuid(),
            DueAt = dueAt,
            CreatedAt = dueAt.AddMinutes(-10),
            Status = ScheduledPipelineTaskStatus.Pending,
        };

    private static StreamEntity NewStream(string id, Guid channelId, DateTimeOffset startedAt) =>
        new()
        {
            Id = id,
            ChannelId = channelId,
            StartedAt = startedAt,
        };
}

/// <summary>
/// A minimal SQLite-backed model exercising exactly the same model-building step
/// (<see cref="ProviderCompatibilityExtensions.ApplySqliteCompatibility"/>) <c>AppDbContext</c> runs for the two
/// entities under test, without pulling in the full ~90-entity graph (and its FK closure) that
/// <c>AppDbContext</c> carries. Navigations are ignored — this proves the column mapping / query-translation
/// fix, not the full relational graph, which the existing <c>PendingModelChangesGuardTests</c> already guards.
/// </summary>
internal sealed class DateTimeOffsetTranslationDbContext(
    DbContextOptions<DateTimeOffsetTranslationDbContext> options
) : DbContext(options)
{
    public DbSet<ScheduledPipelineTask> ScheduledPipelineTasks => Set<ScheduledPipelineTask>();
    public DbSet<StreamEntity> Streams => Set<StreamEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ScheduledPipelineTask>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.VariablesJson).IsRequired();
            b.Property(e => e.TriggeredByUserId).IsRequired();
            b.Property(e => e.TriggeredByDisplayName).IsRequired();
            b.Property(e => e.Status).IsRequired();
        });

        modelBuilder.Entity<StreamEntity>(b =>
        {
            b.HasKey(e => e.Id);
            b.Ignore(e => e.Channel);
            b.Ignore(e => e.Tags);
            b.Ignore(e => e.ContentLabels);
        });

        // Stream.Channel pulls the full Channel navigation graph (Moderators, Events, User, Pronoun, ...) into
        // the model via convention discovery BEFORE this method body runs; ignoring the single navigation above
        // only removes that one edge — the already-discovered related entity types stay in the model with none
        // of their real IEntityTypeConfiguration<T> applied (no keys), so they must be dropped explicitly.
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.Channel>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.User>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelModerator>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelEvent>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.Pronoun>();

        // The exact production step under test — SQLite has no native DateTimeOffset comparison, so this
        // rewrites every DateTimeOffset(?) column to a UTC-ticks INTEGER so relational operators translate.
        modelBuilder.ApplySqliteCompatibility();
    }
}
