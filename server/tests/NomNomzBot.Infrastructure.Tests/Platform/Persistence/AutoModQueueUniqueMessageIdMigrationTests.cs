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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The <c>AutoModQueueUniqueMessageId</c> migration must self-heal a database that already holds several
/// queue rows for one held AutoMod message (the enqueue path used to insert one per hold event) instead of
/// aborting on the new unique index. Proven on a real SQLite file: duplicates are seeded BEFORE the target
/// migration runs, then the test asserts the oldest row survives untouched, later pending duplicates close as
/// expired, other channels and other messages are left alone, and the index rejects a fresh duplicate.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class AutoModQueueUniqueMessageIdMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261008040557_AddLockdownApplyFailed";
    private const string TargetMigrationId = "20261008065503_AutoModQueueUniqueMessageId";

    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000f2"
    );
    private static readonly Guid OldestRow = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid PendingDuplicate = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a2"
    );
    private static readonly Guid ApprovedDuplicate = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a3"
    );
    private static readonly Guid OtherChannelRow = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a4"
    );
    private static readonly Guid OtherMessageRow = Guid.Parse(
        "0192a000-0000-7000-8000-0000000000a5"
    );
    private static readonly Guid FlagRowOne = Guid.Parse("0192a000-0000-7000-8000-0000000000a6");
    private static readonly Guid FlagRowTwo = Guid.Parse("0192a000-0000-7000-8000-0000000000a7");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_automod_unique_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        foreach (
            string path in new[]
            {
                _dbPath,
                $"{_dbPath}-wal",
                $"{_dbPath}-shm",
                $"{_dbPath}-journal",
            }
        )
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private AppDbContext NewContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                ConnectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;
        return new(options);
    }

    private static Task InsertRowAsync(
        AppDbContext context,
        Guid id,
        Guid broadcaster,
        string? messageId,
        ModerationQueueStatus status,
        DateTime createdAt
    )
    {
        // Raw SQL against the schema AS IT STOOD before the target migration: the current model carries
        // later columns, so an EF entity Add here would not match a genuinely pre-existing install.
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "ModerationQueueItems"
                ("Id", "BroadcasterId", "Source", "Status", "AutoModMessageId", "CreatedAt", "UpdatedAt")
            VALUES
                ({id}, {broadcaster}, 0, {(int)status}, {messageId}, {createdAt}, {createdAt})
            """
        );
    }

    [Fact]
    public async Task Migration_keeps_the_oldest_row_expires_later_pending_duplicates_and_enforces_the_index()
    {
        DateTime t0 = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            await InsertRowAsync(
                seedContext,
                OldestRow,
                Broadcaster,
                "msg-held",
                ModerationQueueStatus.Pending,
                t0
            );
            await InsertRowAsync(
                seedContext,
                PendingDuplicate,
                Broadcaster,
                "msg-held",
                ModerationQueueStatus.Pending,
                t0.AddMinutes(1)
            );
            await InsertRowAsync(
                seedContext,
                ApprovedDuplicate,
                Broadcaster,
                "msg-held",
                ModerationQueueStatus.Approved,
                t0.AddMinutes(2)
            );
            await InsertRowAsync(
                seedContext,
                OtherChannelRow,
                OtherBroadcaster,
                "msg-held",
                ModerationQueueStatus.Pending,
                t0.AddMinutes(3)
            );
            await InsertRowAsync(
                seedContext,
                OtherMessageRow,
                Broadcaster,
                "msg-other",
                ModerationQueueStatus.Pending,
                t0.AddMinutes(4)
            );
            await InsertRowAsync(
                seedContext,
                FlagRowOne,
                Broadcaster,
                null,
                ModerationQueueStatus.Pending,
                t0.AddMinutes(5)
            );
            await InsertRowAsync(
                seedContext,
                FlagRowTwo,
                Broadcaster,
                null,
                ModerationQueueStatus.Pending,
                t0.AddMinutes(6)
            );
        }

        await using (AppDbContext migrateContext = NewContext())
        {
            IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
            Func<Task> act = () => migrator.MigrateAsync(TargetMigrationId);
            await act.Should()
                .NotThrowAsync("pre-existing duplicate holds must not brick migration");
            await migrator.MigrateAsync();
        }

        await using AppDbContext assertContext = NewContext();
        Dictionary<Guid, ModerationQueueItem> rows = await assertContext
            .Set<ModerationQueueItem>()
            .IgnoreQueryFilters()
            .ToDictionaryAsync(r => r.Id);

        rows.Should().HaveCount(7, "the clean-up closes or detaches rows, it never deletes them");

        ModerationQueueItem oldest = rows[OldestRow];
        oldest.Status.Should().Be(ModerationQueueStatus.Pending);
        oldest.AutoModMessageId.Should().Be("msg-held");
        oldest.ResolutionAction.Should().BeNull();
        oldest.ResolvedAt.Should().BeNull();

        ModerationQueueItem expired = rows[PendingDuplicate];
        expired.Status.Should().Be(ModerationQueueStatus.Expired);
        expired.ResolutionAction.Should().Be("expired");
        expired.ResolvedAt.Should().NotBeNull();
        expired.AutoModMessageId.Should().BeNull();

        ModerationQueueItem approved = rows[ApprovedDuplicate];
        approved.Status.Should().Be(ModerationQueueStatus.Approved);
        approved.ResolutionAction.Should().BeNull();
        approved.AutoModMessageId.Should().BeNull();

        rows[OtherChannelRow].Status.Should().Be(ModerationQueueStatus.Pending);
        rows[OtherChannelRow].AutoModMessageId.Should().Be("msg-held");
        rows[OtherMessageRow].Status.Should().Be(ModerationQueueStatus.Pending);
        rows[OtherMessageRow].AutoModMessageId.Should().Be("msg-other");
        rows[FlagRowOne].Status.Should().Be(ModerationQueueStatus.Pending);
        rows[FlagRowTwo].Status.Should().Be(ModerationQueueStatus.Pending);

        rows.Values.Count(r =>
                r.Status == ModerationQueueStatus.Pending
                && r.BroadcasterId == Broadcaster
                && r.AutoModMessageId == "msg-held"
            )
            .Should()
            .Be(1, "exactly one pending row remains for the duplicated held message");

        string indexSql = await assertContext
            .Database.SqlQueryRaw<string>(
                "SELECT sql AS \"Value\" FROM sqlite_master WHERE type = 'index' "
                    + "AND name = 'IX_ModerationQueueItems_BroadcasterId_AutoModMessageId'"
            )
            .SingleAsync();
        indexSql.Should().Contain("UNIQUE");

        Func<Task> insertDuplicate = () =>
            InsertRowAsync(
                assertContext,
                Guid.CreateVersion7(),
                Broadcaster,
                "msg-held",
                ModerationQueueStatus.Pending,
                t0.AddMinutes(10)
            );
        await insertDuplicate
            .Should()
            .ThrowAsync<SqliteException>(
                "the unique index must reject a second row for one held message"
            );
    }
}
