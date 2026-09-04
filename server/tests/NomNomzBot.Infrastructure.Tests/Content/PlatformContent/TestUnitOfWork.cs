// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// A minimal <see cref="IUnitOfWork"/> over a plain <see cref="IApplicationDbContext"/> test fixture —
/// <c>UnitOfWork</c> itself requires a concrete <c>AppDbContext</c>, which the lightweight per-test SQLite
/// fixtures are not. Only <see cref="SaveChangesAsync"/> is exercised by <c>PlatformContentService</c>;
/// the transaction members are not, so they run the operation directly with no real transaction — enough
/// for these tests' single-connection in-memory SQLite database.
/// </summary>
internal sealed class TestUnitOfWork(IApplicationDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default
    ) => await operation(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default,
        Func<T, bool>? shouldCommit = null
    ) => await operation(cancellationToken);

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
