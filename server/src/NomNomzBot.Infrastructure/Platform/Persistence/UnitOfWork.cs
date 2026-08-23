// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

public class UnitOfWork : IUnitOfWork, IAsyncDisposable, IDisposable
{
    private readonly AppDbContext _db;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    // S038: without this guard a second BeginTransactionAsync silently overwrites _transaction with a new
    // EF Core "nested" transaction object (SQLite/Npgsql have no true nested transactions — the second Begin
    // either throws deep in the provider or, worse, quietly reuses the same underlying connection
    // transaction), so a caller that begins twice loses the ability to roll back its outer scope: committing
    // or rolling back only ever touches the innermost handle this field still points to. Rejecting the
    // second Begin up front surfaces the bug at the call site instead of at a random later transaction fault.
    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException(
                "A transaction is already active on this UnitOfWork — nested BeginTransactionAsync is not supported. "
                    + "Commit or roll back the current transaction first."
            );

        _transaction = await _db.Database.BeginTransactionAsync(ct);
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default
    ) => _db.ExecuteInTransactionAsync(operation, ct);

    public Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default,
        Func<T, bool>? shouldCommit = null
    ) => _db.ExecuteInTransactionAsync(operation, ct, shouldCommit);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _transaction?.Dispose();
        _transaction = null;

        GC.SuppressFinalize(this);
    }
}
