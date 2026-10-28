// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
