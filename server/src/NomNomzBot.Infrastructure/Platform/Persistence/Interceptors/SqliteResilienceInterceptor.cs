// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

/// <summary>
/// S038: SQLite is the self-host default runtime (a file beside the binary, no server process), so
/// without WAL journaling a writer holds an exclusive lock for the duration of its transaction and any
/// concurrent writer — a second request thread, a background hosted service — fails immediately with
/// "database is locked" rather than waiting its turn. Every new ADO connection this DbContext opens is
/// stamped with WAL mode (readers no longer block writers, writers queue instead of erroring) plus a
/// busy timeout (SQLite's own retry-and-wait loop before it gives up and throws SQLITE_BUSY), covering
/// connections opened by EF Core itself and by any raw <c>Database.GetDbConnection()</c> caller.
/// </summary>
public sealed class SqliteResilienceInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// How long SQLite retries against a lock before raising SQLITE_BUSY. It is set FIRST in the pragma
    /// batch, before <c>journal_mode</c>: switching journal mode itself takes a lock, so with the old order
    /// that very statement could throw "database is locked" under concurrent opens — the one moment no busy
    /// timeout was in effect yet.
    ///
    /// 30s, not 5s: SQLite serialises writers, so the wait a writer must absorb is the sum of everyone
    /// ahead of it, not one lock hold. 5s was survivable on a fast dev box and not on a loaded CI runner,
    /// where 40 concurrent writers took 29s to drain and the tail of them got SQLITE_BUSY. Waiting costs a
    /// slow write; not waiting costs the user a failed action, so the timeout is sized for the queue.
    /// </summary>
    private const int BusyTimeoutMilliseconds = 30000;

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData
    ) => ApplyPragmas(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => await ApplyPragmasAsync(connection, cancellationToken);

    private static void ApplyPragmas(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA busy_timeout={BusyTimeoutMilliseconds}; PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA busy_timeout={BusyTimeoutMilliseconds}; PRAGMA journal_mode=WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
