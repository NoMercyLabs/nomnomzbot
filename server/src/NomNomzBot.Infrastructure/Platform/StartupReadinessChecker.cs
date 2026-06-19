// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;

namespace NomNomzBot.Infrastructure.Platform;

public sealed class StartupReadinessChecker(
    IConfiguration configuration,
    ILogger<StartupReadinessChecker> logger
)
{
    private const int MaxAttempts = 10;

    public async Task WaitForPostgresAsync(CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning(
                "No PostgreSQL connection string configured — skipping readiness check"
            );
            return;
        }

        TimeSpan delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using NpgsqlConnection conn = new(connectionString);
                await conn.OpenAsync(cancellationToken);
                logger.LogInformation("PostgreSQL is ready");
                return;
            }
            catch (PostgresException pgEx)
                when (pgEx.SqlState == "28P01" || pgEx.SqlState == "28000")
            {
                // Auth failure — retrying won't help, the password is wrong.
                throw new InvalidOperationException(
                    "PostgreSQL authentication failed. The database volume likely has credentials "
                        + "that don't match the current configuration. "
                        + "Reset the volume with: docker compose down -v && docker compose up -d",
                    pgEx
                );
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "PostgreSQL not ready (attempt {Attempt}/{Max}): {Message}",
                    attempt,
                    MaxAttempts,
                    ex.Message
                );
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        throw new InvalidOperationException(
            "PostgreSQL is not reachable after multiple attempts. "
                + "Start postgres via 'docker-compose up -d postgres' "
                + "or check ConnectionStrings__DefaultConnection in your .env file."
        );
    }

    public async Task WaitForRedisAsync(CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogInformation("No Redis connection string configured — using in-memory cache");
            return;
        }

        TimeSpan delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = true;
                options.ConnectTimeout = 3000;

                using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(
                    options
                );
                await redis.GetDatabase().PingAsync();
                logger.LogInformation("Redis is ready");
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    "Redis not ready (attempt {Attempt}/{Max}): {Message}",
                    attempt,
                    MaxAttempts,
                    ex.Message
                );
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        throw new InvalidOperationException(
            "Redis is not reachable after multiple attempts. "
                + "Start redis via 'docker-compose up -d redis' "
                + "or check ConnectionStrings__Redis in your .env file."
        );
    }
}
