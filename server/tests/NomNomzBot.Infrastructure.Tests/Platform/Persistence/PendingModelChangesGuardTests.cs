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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S117 — guards against the entity model drifting away from its migrations without a migration to catch up.
/// This project ships TWO migration assemblies for one <see cref="AppDbContext"/> model — the Postgres set in
/// <c>NomNomzBot.Infrastructure/Platform/Persistence/Migrations</c> (self_host_full / saas) and the SQLite set
/// in <c>NomNomzBot.Migrations.Sqlite</c> (self_host_lite). It was found that <c>Rewards.IsUserInputRequired</c>
/// was added to the model without its own migration and got silently swept up by the next, unrelated migration
/// (<c>AddIamAuditLogTargetPrincipalAndRole</c>) instead. <see cref="DbContext.Database"/>'s
/// <c>GetPendingModelChanges()</c> is exactly the check <c>dotnet ef migrations has-pending-model-changes</c> runs;
/// asserting it is empty for both providers on every test run makes the next silent drift a red test instead of a
/// bundled surprise in someone else's migration.
/// </summary>
public sealed class PendingModelChangesGuardTests
{
    [Fact]
    public void Postgres_migration_set_has_no_pending_model_changes()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nomnomzbot;Username=postgres;Password=postgres",
                npgsqlOptions =>
                    npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
            )
            .Options;

        using AppDbContext context = new(options);

        context
            .Database.HasPendingModelChanges()
            .Should()
            .BeFalse(
                "the entity model must not drift from the Postgres migration set — add a migration when the model changes"
            );
    }

    [Fact]
    public void Sqlite_migration_set_has_no_pending_model_changes()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                "Data Source=:memory:",
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;

        using AppDbContext context = new(options);

        context
            .Database.HasPendingModelChanges()
            .Should()
            .BeFalse(
                "the entity model must not drift from the SQLite migration set — add a migration when the model changes"
            );
    }
}
