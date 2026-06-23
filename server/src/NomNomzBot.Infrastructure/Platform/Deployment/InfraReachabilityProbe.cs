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
using NomNomzBot.Application.Common.Interfaces;
using Npgsql;
using StackExchange.Redis;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The live reachability probe (deployment-distribution §2). A single short-timeout connection attempt against the
/// configured Postgres + Redis connection strings; any failure (or an unconfigured / SQLite connection string)
/// reports <c>false</c> so the detector falls back to lite. Never throws — reachability is a yes/no signal.
/// </summary>
public sealed class InfraReachabilityProbe : IInfraReachabilityProbe
{
    private const int ProbeTimeoutMs = 1500;

    private readonly IConfiguration _configuration;
    private readonly ILogger<InfraReachabilityProbe> _logger;

    public InfraReachabilityProbe(
        IConfiguration configuration,
        ILogger<InfraReachabilityProbe> logger
    )
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsPostgresReachableAsync(CancellationToken cancellationToken = default)
    {
        string? connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString) || !LooksLikePostgres(connectionString))
            return false;

        try
        {
            NpgsqlConnectionStringBuilder builder = new(connectionString)
            {
                Timeout = ProbeTimeoutMs / 1000,
                CommandTimeout = ProbeTimeoutMs / 1000,
            };
            await using NpgsqlConnection conn = new(builder.ConnectionString);
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken
            );
            cts.CancelAfter(ProbeTimeoutMs);
            await conn.OpenAsync(cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Postgres not reachable during profile probe: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> IsRedisReachableAsync(CancellationToken cancellationToken = default)
    {
        string? connectionString =
            _configuration.GetConnectionString("Redis") ?? _configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = ProbeTimeoutMs;
            options.ConnectRetry = 0;

            await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(
                options
            );
            await redis.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Redis not reachable during profile probe: {Message}", ex.Message);
            return false;
        }
    }

    // A real Postgres connection string carries Host=/Server=; a SQLite "Data Source=..." must never be probed
    // as Postgres (it is the lite signal).
    private static bool LooksLikePostgres(string connectionString) =>
        connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase);
}
