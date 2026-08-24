// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Api.HealthChecks;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.Tests.HealthChecks;

/// <summary>
/// Proves <see cref="PendingMigrationsHealthCheck"/> against a REAL SQLite database and the REAL
/// <c>NomNomzBot.Migrations.Sqlite</c> migration set (the same assembly <c>Program.cs</c> wires via
/// <c>MigrationsAssembly("NomNomzBot.Migrations.Sqlite")</c>) — not a mock — so the assertion is about the
/// actual outcome an orchestrator sees: 503 while schema migrations are pending, 200 once applied.
/// </summary>
public sealed class PendingMigrationsHealthCheckTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PendingMigrationsHealthCheckTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    _connection,
                    sqliteOptions =>
                        sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
                )
                .Options
        );

    [Fact]
    public async Task PendingMigrations_ReportUnhealthy()
    {
        await using AppDbContext dbContext = CreateContext();
        // A fresh :memory: connection has no schema at all — every migration is pending.
        PendingMigrationsHealthCheck check = new(dbContext);

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("pending migration");
    }

    [Fact]
    public async Task AppliedMigrations_ReportHealthy()
    {
        await using AppDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        PendingMigrationsHealthCheck check = new(dbContext);

        HealthCheckResult result = await check.CheckHealthAsync(new());

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
