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
using Microsoft.EntityFrameworkCore.Storage;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S038: <see cref="UnitOfWork"/> previously had no guard against a caller invoking
/// <see cref="UnitOfWork.BeginTransactionAsync"/> a second time before committing or rolling back the
/// first — the second call silently overwrote the private <c>_transaction</c> field, orphaning the first
/// <see cref="IDbContextTransaction"/> (never committed, never rolled back, its underlying connection
/// transaction held open until GC finalizes it) and leaving Commit/Rollback only ever able to reach the
/// innermost handle. It also implemented neither <see cref="IDisposable"/> nor
/// <see cref="IAsyncDisposable"/>, so a caller that opened a transaction and then threw before
/// Commit/Rollback leaked the transaction for the same reason. Both are fixed here: nested Begin is
/// rejected outright (chosen over silent re-nesting because neither SQLite nor Npgsql supports true nested
/// transactions — pretending to nest would just be the same bug with extra steps), and disposal now
/// releases whatever transaction handle is still open.
/// </summary>
public sealed class UnitOfWorkTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_uow_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath}";

    public void Dispose()
    {
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private async Task<AppDbContext> NewContextAsync()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        AppDbContext db = new(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    [Fact]
    public async Task Beginning_a_second_transaction_before_the_first_resolves_throws()
    {
        await using AppDbContext db = await NewContextAsync();
        await using UnitOfWork sut = new(db);

        await sut.BeginTransactionAsync();

        Func<Task> secondBegin = () => sut.BeginTransactionAsync();

        await secondBegin
            .Should()
            .ThrowAsync<InvalidOperationException>(
                "a second Begin before Commit/Rollback would silently orphan the first transaction handle"
            );

        // The original transaction must still be the one in charge — it can still be committed cleanly.
        await sut.CommitTransactionAsync();
    }

    [Fact]
    public async Task After_commit_a_new_transaction_can_be_begun_again()
    {
        await using AppDbContext db = await NewContextAsync();
        await using UnitOfWork sut = new(db);

        await sut.BeginTransactionAsync();
        await sut.CommitTransactionAsync();

        Func<Task> secondBegin = () => sut.BeginTransactionAsync();

        await secondBegin
            .Should()
            .NotThrowAsync("the slot is free again once the first transaction resolved");

        await sut.RollbackTransactionAsync();
    }

    [Fact]
    public async Task Disposing_releases_an_open_transaction_without_requiring_commit_or_rollback()
    {
        AppDbContext db = await NewContextAsync();
        UnitOfWork sut = new(db);

        await sut.BeginTransactionAsync();

        // No Commit/Rollback — simulates a caller that threw between Begin and resolving the transaction.
        // DisposeAsync must still tear the transaction down instead of leaking the underlying handle.
        await sut.DisposeAsync();

        // Proven from the outside: a FRESH UnitOfWork against the same DbContext can begin a transaction
        // immediately. If DisposeAsync had left the old transaction's connection-level transaction open,
        // SQLite would reject/queue this Begin against the still-locked connection.
        UnitOfWork afterDispose = new(db);
        Func<Task> beginAfterDispose = () => afterDispose.BeginTransactionAsync();

        await beginAfterDispose
            .Should()
            .NotThrowAsync("disposing the first UnitOfWork must release its transaction");

        await afterDispose.RollbackTransactionAsync();
        await db.DisposeAsync();
    }
}
