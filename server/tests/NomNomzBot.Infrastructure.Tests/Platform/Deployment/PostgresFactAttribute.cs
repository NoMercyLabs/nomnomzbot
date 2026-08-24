// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Npgsql;

namespace NomNomzBot.Infrastructure.Tests.Platform.Deployment;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself unless a real Postgres server is reachable at
/// <see cref="PostgresRunOnceGuardTests.ConnectionString"/> (the local dev container by default,
/// or <c>NNZ_TEST_PG_CONNECTION</c> for CI). Modeled on
/// <c>NomNomzBot.E2E.Tests.Harness.E2EFactAttribute</c> — same "skip unless the live dependency is
/// present" shape, but the gate here is a genuine connectivity probe (short timeout) rather than a
/// static env-var flag, because there is no single "Postgres is configured" switch: a dev machine
/// either has the container running or it doesn't. Skipping (never silently passing, never failing)
/// keeps CI green with no Postgres service while still proving the real
/// <c>pg_try_advisory_lock</c> cross-process exclusion wherever Postgres IS reachable.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!IsReachable(PostgresRunOnceGuardTests.ConnectionString))
        {
            Skip =
                "No reachable Postgres server at the configured connection string. Start the local "
                + "dev container (`docker compose up -d postgres`) or set NNZ_TEST_PG_CONNECTION to "
                + "run this test for real.";
        }
    }

    private static bool IsReachable(string connectionString)
    {
        try
        {
            NpgsqlConnectionStringBuilder builder = new(connectionString)
            {
                Timeout = 2,
                CommandTimeout = 2,
            };
            using NpgsqlConnection connection = new(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
