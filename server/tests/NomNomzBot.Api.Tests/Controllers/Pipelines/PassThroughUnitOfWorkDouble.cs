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

namespace NomNomzBot.Api.Tests.Controllers.Pipelines;

/// <summary>
/// An <see cref="IUnitOfWork"/> double that runs the wrapped operation exactly once and hands back its
/// result, mirroring the production happy path, for the <c>PipelinesController</c> HTTP round-trip test
/// (S-PIPE-BLANK-b). Copied from the Infrastructure.Tests <c>PassThroughUnitOfWork</c> since Api.Tests does
/// not reference that test project.
/// </summary>
internal sealed class PassThroughUnitOfWorkDouble : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

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
