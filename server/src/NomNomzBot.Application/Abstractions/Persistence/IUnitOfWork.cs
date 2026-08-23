// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction, as a RETRIABLE unit. This is the only
    /// safe way to open a transaction when the provider has a retrying execution strategy configured
    /// (Npgsql does — <c>EnableRetryOnFailure</c>): a bare Begin/Commit pair throws
    /// "the configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
    /// user-initiated transactions" the moment the first query runs, which on Postgres took down the
    /// whole API at boot when the content seeder did exactly that. The operation may be invoked more
    /// than once (that is what a retry IS), so it must be idempotent and must not depend on state
    /// captured before the first attempt. On any exception the transaction is rolled back and the
    /// exception rethrown.
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="ExecuteInTransactionAsync(Func{CancellationToken,Task},CancellationToken)"/>
    /// <returns>Whatever <paramref name="operation"/> produced, once its transaction committed.</returns>
    /// <remarks>
    /// <paramref name="shouldCommit"/> decides, from the operation's own result, whether to commit.
    /// Services that report failure as a <c>Result</c> rather than an exception pass
    /// <c>r => r.IsSuccess</c> so a business failure rolls back every write the attempt made.
    /// </remarks>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default,
        Func<T, bool>? shouldCommit = null
    );
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
