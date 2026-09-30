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
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S038: SQLite (the self-host default runtime) opens each writer with an exclusive lock for the
/// duration of its transaction; without WAL journaling + a busy timeout, a second concurrent writer gets
/// "database is locked" (SQLITE_BUSY) immediately instead of waiting its turn. This tests
/// <see cref="SqliteResilienceInterceptor"/> — the exact interceptor <c>AddInfrastructure</c> wires onto
/// every SQLite <c>AppDbContext</c> — against real concurrent writers spanning TWO independent tables
/// (standing in for two different services writing at the same time), each writer opening its own
/// connection against one shared file database, per the house SqliteFileConcurrency harness style.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class SqliteResilienceInterceptorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_sqlite_soak_{Guid.NewGuid():N}.db"
    );

    // Microsoft.Data.Sqlite's own ADO-level "Default Timeout" (30s by default) retries a SQLITE_BUSY command on
    // its own, independent of the sqlite3 busy_timeout pragma — a generous default here would swallow "database
    // is locked" regardless of whether SqliteResilienceInterceptor ran, and "0" is the ADO.NET convention for
    // "wait forever" (NOT "fail immediately"), which would hang the test instead of failing it. 5 seconds is
    // short enough that it cannot itself paper over a missing interceptor (real contention without WAL/busy_timeout
    // exceeds it), while comfortably longer than this workload takes to queue and drain when the interceptor's
    // WAL mode + 5s sqlite busy_timeout ARE in effect.
    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=5";

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

    private SoakDbContext NewContext(bool withResilience)
    {
        DbContextOptionsBuilder<SoakDbContext> builder =
            new DbContextOptionsBuilder<SoakDbContext>().UseSqlite(ConnectionString);
        if (withResilience)
            builder.AddInterceptors(new SqliteResilienceInterceptor());
        return new(builder.Options);
    }

    [Fact]
    public async Task Opening_a_connection_through_the_interceptor_sets_WAL_mode_and_the_busy_timeout()
    {
        using (SoakDbContext schema = NewContext(withResilience: true))
            await schema.Database.EnsureCreatedAsync();

        await using SoakDbContext db = NewContext(withResilience: true);
        await db.Database.OpenConnectionAsync();

        string journalMode = await ScalarAsync(db, "PRAGMA journal_mode;");
        string busyTimeout = await ScalarAsync(db, "PRAGMA busy_timeout;");
        string synchronous = await ScalarAsync(db, "PRAGMA synchronous;");

        journalMode
            .Should()
            .BeEquivalentTo("wal", "the interceptor must stamp WAL on every opened connection");
        busyTimeout
            .Should()
            .Be(
                "30000",
                "the interceptor must give SQLite room to wait out a lock before giving up"
            );
        synchronous
            .Should()
            .Be(
                "1",
                "WAL pairs with synchronous=NORMAL (1), so a commit does not wait for a disk flush"
            );
    }

    [Fact]
    public async Task A_writer_blocked_by_a_long_transaction_on_another_table_waits_its_turn_instead_of_failing()
    {
        // Deterministic contention, not a load soak: writer A holds an open write transaction on table A for
        // longer than the ADO-level Default Timeout (5s), and writer B writes table B meanwhile. SQLite has one
        // writer at a time across the whole file, so B MUST wait for A. Without the interceptor's busy_timeout B
        // gives up after ~5s with "database is locked"; with it B queues and lands once A commits. The earlier
        // 40-writer soak proved the same property, but its drain time scaled with runner speed and reached the
        // 30s busy timeout on CI; this shape takes ~7s everywhere.
        using (SoakDbContext schema = NewContext(withResilience: true))
            await schema.Database.EnsureCreatedAsync();

        TimeSpan holdTime = TimeSpan.FromSeconds(7);
        using SemaphoreSlim aHoldsTheWriteLock = new(0, 1);

        Task writerA = Task.Run(async () =>
        {
            await using SoakDbContext db = NewContext(withResilience: true);
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
                await db.Database.BeginTransactionAsync();
            db.CountersA.Add(new() { Label = "a-holder", Value = 1 });
            await db.SaveChangesAsync();
            aHoldsTheWriteLock.Release();
            await Task.Delay(holdTime);
            await tx.CommitAsync();
        });

        await aHoldsTheWriteLock.WaitAsync();
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        await using (SoakDbContext db = NewContext(withResilience: true))
        {
            db.CountersB.Add(new() { Label = "b-waiter", Value = 2 });
            Func<Task> write = () => db.SaveChangesAsync();
            await write
                .Should()
                .NotThrowAsync(
                    "busy_timeout must make a second writer wait out the first writer's lock instead of failing"
                );
        }
        waited.Stop();
        await writerA;

        waited
            .Elapsed.Should()
            .BeGreaterThan(
                TimeSpan.FromSeconds(5),
                "the write genuinely queued behind writer A's lock (longer than the 5s ADO timeout), so the test proved contention"
            );
        await using SoakDbContext verify = NewContext(withResilience: true);
        (await verify.CountersA.CountAsync()).Should().Be(1, "writer A's row committed");
        (await verify.CountersB.CountAsync()).Should().Be(1, "writer B's row landed after waiting");
    }

    private static async Task<string> ScalarAsync(DbContext db, string sql)
    {
        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        await using System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }
}

internal sealed class SoakDbContext(DbContextOptions<SoakDbContext> options) : DbContext(options)
{
    public DbSet<SoakCounterA> CountersA => Set<SoakCounterA>();
    public DbSet<SoakCounterB> CountersB => Set<SoakCounterB>();
}

internal sealed class SoakCounterA
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

internal sealed class SoakCounterB
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}
