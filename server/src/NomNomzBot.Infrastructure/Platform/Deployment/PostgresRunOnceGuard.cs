// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using Npgsql;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The full/SaaS run-once guard (platform-conventions §3.8): a session-scoped Postgres advisory lock
/// (<c>pg_try_advisory_lock</c>) so exactly one instance owns a named resource (migrate / seed /
/// conduit-provision) across the stateless pool. The lock is released when its dedicated connection is disposed
/// (lease dispose). A held lock returns <c>null</c> (another instance owns it).
/// </summary>
public sealed class PostgresRunOnceGuard : IRunOnceGuard
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresRunOnceGuard> _logger;

    public PostgresRunOnceGuard(IConfiguration configuration, ILogger<PostgresRunOnceGuard> logger)
    {
        _connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "PostgresRunOnceGuard requires ConnectionStrings:DefaultConnection."
            );
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceName,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        long lockKey = StableLockKey(resourceName);

        NpgsqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", lockKey);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        bool acquired = result is true;

        if (!acquired)
        {
            await connection.DisposeAsync();
            _logger.LogDebug(
                "Advisory lock for '{Resource}' is held by another instance.",
                resourceName
            );
            return null;
        }

        _logger.LogDebug("Acquired advisory lock for '{Resource}'.", resourceName);
        return new AdvisoryLockLease(connection, lockKey);
    }

    // Hash the resource name to a stable 64-bit advisory-lock key (process-independent, so all instances agree).
    private static long StableLockKey(string resourceName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceName));
        return BitConverter.ToInt64(hash, 0);
    }

    private sealed class AdvisoryLockLease : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _lockKey;

        public AdvisoryLockLease(NpgsqlConnection connection, long lockKey)
        {
            _connection = connection;
            _lockKey = lockKey;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using NpgsqlCommand command = _connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key)";
                command.Parameters.AddWithValue("key", _lockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
