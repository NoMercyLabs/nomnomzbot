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

namespace NomNomzBot.Infrastructure.Platform.Persistence;

/// <summary>
/// The one way this codebase opens a database transaction. Npgsql is configured with
/// <c>EnableRetryOnFailure</c>, and its <c>NpgsqlRetryingExecutionStrategy</c> refuses a
/// user-initiated <c>BeginTransactionAsync</c> outright — the call throws
/// "does not support user-initiated transactions" as soon as the first query runs. That is a
/// Postgres-only failure, so every such call site looks perfectly healthy against the SQLite used in
/// development and tests and only detonates in a deployed environment (it took the API down at boot
/// when the content seeder did it).
/// <para>
/// Running the whole transaction through <see cref="DatabaseFacade.CreateExecutionStrategy"/> is EF
/// Core's sanctioned answer: the strategy owns the retry loop, and each attempt gets its own fresh
/// transaction. <c>operation</c> can therefore run MORE THAN ONCE — it must be
/// idempotent, must derive nothing from state captured before the first attempt, and must not fire
/// outward side effects (events, HTTP calls, chat messages); those belong after the call returns.
/// </para>
/// <para>
/// <see cref="UnitOfWork.ExecuteInTransactionAsync(Func{CancellationToken,Task},CancellationToken)"/>
/// is the seam for services that inject <c>IUnitOfWork</c>; this extension is the same mechanism for
/// services that hold a <see cref="DbContext"/> directly (guarded writes that pair an
/// <c>ExecuteUpdateAsync</c> with its read-back).
/// </para>
/// </summary>
public static class RetriableTransaction
{
    /// <inheritdoc cref="RetriableTransaction"/>
    /// <remarks>
    /// <c>shouldCommit</c> decides, from the operation's own result, whether the transaction commits.
    /// Services that signal failure with a <c>Result</c> instead of an exception pass
    /// <c>r => r.IsSuccess</c>, so a business failure rolls back every write the attempt made —
    /// without dressing an expected outcome up as an exception.
    /// </remarks>
    public static Task<T> ExecuteInTransactionAsync<T>(
        this DbContext db,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default,
        Func<T, bool>? shouldCommit = null
    ) =>
        db
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    await using IDbContextTransaction transaction =
                        await db.Database.BeginTransactionAsync(token);
                    try
                    {
                        T result = await operation(token);
                        if (shouldCommit is null || shouldCommit(result))
                            await transaction.CommitAsync(token);
                        else
                            await transaction.RollbackAsync(token);
                        return result;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(token);
                        throw;
                    }
                },
                cancellationToken
            );

    /// <inheritdoc cref="RetriableTransaction"/>
    public static Task ExecuteInTransactionAsync(
        this DbContext db,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default
    ) =>
        db.ExecuteInTransactionAsync<bool>(
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken
        );
}
