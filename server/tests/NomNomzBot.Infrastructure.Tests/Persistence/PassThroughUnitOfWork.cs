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

namespace NomNomzBot.Infrastructure.Tests.Persistence;

/// <summary>
/// A <see cref="IUnitOfWork"/> double for tests that don't care about transaction boundaries but DO
/// need the work inside one to actually run. A mocking-framework substitute returns default for
/// <c>ExecuteInTransactionAsync</c> — null for the generic overload — which silently swallows the
/// entire unit of work and fails the test somewhere far away with a null-reference. This runs the
/// operation exactly once and hands back its result, which is what the production implementation does
/// on the happy path. Tests that need to prove commit/rollback use a harness with a real DbContext
/// transaction instead.
/// </summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    /// <summary>How many times a caller wrapped work in a transaction — enough to assert that a write
    /// path is transactional at all, without pinning the boundary semantics.</summary>
    public int TransactionCount { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default
    )
    {
        TransactionCount++;
        await operation(cancellationToken);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default,
        Func<T, bool>? shouldCommit = null
    )
    {
        TransactionCount++;
        T result = await operation(cancellationToken);
        if (shouldCommit is not null && !shouldCommit(result))
            RolledBackCount++;
        return result;
    }

    /// <summary>How many transactions the service asked to roll back by returning a failed result —
    /// the distinction a test needs to prove a failed write path undid its own work.</summary>
    public int RolledBackCount { get; private set; }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
