// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Configuration;
using Npgsql;

namespace NomNomzBot.Api.HealthChecks;

/// <summary>
/// Health check that verifies PostgreSQL connectivity by executing a simple query.
/// </summary>
public sealed class DatabaseHealthCheck(IOptions<DatabaseOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using NpgsqlConnection connection = new(options.Value.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("PostgreSQL connection is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", exception: ex);
        }
    }
}
