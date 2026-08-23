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
using NomNomzBot.Infrastructure.Tests.Common;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S038: SQLite (the self-host default runtime) opens each writer with an exclusive lock for the
/// duration of its transaction; without WAL journaling + a busy timeout, a second concurrent writer gets
/// "database is locked" (SQLITE_BUSY) immediately instead of waiting its turn. This soaks
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

    private string ConnectionString => $"Data Source={_dbPath}";

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

        journalMode
            .Should()
            .BeEquivalentTo("wal", "the interceptor must stamp WAL on every opened connection");
        busyTimeout
            .Should()
            .Be(
                "5000",
                "the interceptor must give SQLite room to wait out a lock before giving up"
            );
    }

    [Fact]
    public async Task Concurrent_writers_across_two_tables_produce_zero_database_is_locked_errors()
    {
        using (SoakDbContext schema = NewContext(withResilience: true))
            await schema.Database.EnsureCreatedAsync();

        const int writersPerTable = 20;
        const int rowsPerWriter = 5;
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        IEnumerable<Task> tableAWriters = Enumerable
            .Range(0, writersPerTable)
            .Select(writer =>
                Task.Run(async () =>
                {
                    await using SoakDbContext db = NewContext(withResilience: true);
                    for (int row = 0; row < rowsPerWriter; row++)
                    {
                        db.CountersA.Add(new() { Label = $"a-{writer}-{row}", Value = row });
                        await db.SaveChangesAsync();
                    }
                })
            );

        IEnumerable<Task> tableBWriters = Enumerable
            .Range(0, writersPerTable)
            .Select(writer =>
                Task.Run(async () =>
                {
                    await using SoakDbContext db = NewContext(withResilience: true);
                    for (int row = 0; row < rowsPerWriter; row++)
                    {
                        db.CountersB.Add(new() { Label = $"b-{writer}-{row}", Value = row });
                        await db.SaveChangesAsync();
                    }
                })
            );

        Task[] allWriters = tableAWriters.Concat(tableBWriters).ToArray();

        List<Exception> lockedErrors = [];
        try
        {
            await Task.WhenAll(allWriters);
        }
        catch
        {
            foreach (Task task in allWriters)
            {
                if (task.Exception is not null)
                    lockedErrors.AddRange(task.Exception.InnerExceptions);
            }
        }
        stopwatch.Stop();

        lockedErrors
            .Should()
            .BeEmpty(
                "WAL + busy_timeout must let {0} concurrent writers ({1}ms elapsed) queue instead of erroring",
                allWriters.Length,
                stopwatch.ElapsedMilliseconds
            );

        await using SoakDbContext verify = NewContext(withResilience: true);
        int countA = await verify.CountersA.CountAsync();
        int countB = await verify.CountersB.CountAsync();
        countA
            .Should()
            .Be(
                writersPerTable * rowsPerWriter,
                "every writer on table A must have landed every row"
            );
        countB
            .Should()
            .Be(
                writersPerTable * rowsPerWriter,
                "every writer on table B must have landed every row"
            );
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
