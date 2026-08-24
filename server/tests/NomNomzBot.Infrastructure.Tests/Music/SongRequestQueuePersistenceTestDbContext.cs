// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A real SQLite-backed <see cref="AppDbContext"/> for the S001b persistence tests. The InMemory
/// provider used by every other Music test double does not support
/// <c>ExecuteDeleteAsync</c>/transactions the way <see cref="Infrastructure.Music.SongRequestQueuePersistence"/>
/// relies on for its crash-safety guarantee, so these tests run against a real (file-backed, temp-path)
/// SQLite database instead — proving the actual write strategy (transactional delete-then-insert) that
/// ships on the self-host-lite profile, not an InMemory-provider stand-in for it.
/// </summary>
internal sealed class SongRequestQueuePersistenceTestDbContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    private SongRequestQueuePersistenceTestDbContext(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    /// <summary>Opens a fresh, migrated, per-test SQLite database over an open in-memory connection (kept
    /// open for the object's lifetime — closing it would drop the SQLite ":memory:" database).</summary>
    public static SongRequestQueuePersistenceTestDbContext Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        AppDbContext db = new(options);
        db.Database.EnsureCreated();

        return new(connection, db);
    }

    /// <summary>Opens a second <see cref="AppDbContext"/> over the SAME connection — simulating the next
    /// DI scope after a restart, the way the real singleton store/scoped persistence split works.</summary>
    public AppDbContext OpenNewScope()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new(options);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
