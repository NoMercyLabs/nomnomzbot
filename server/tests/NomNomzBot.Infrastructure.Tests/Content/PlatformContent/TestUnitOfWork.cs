// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
