// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Migrations.Sqlite;

/// <summary>
/// Design-time factory that targets SQLite, so <c>dotnet ef migrations add … -p NomNomzBot.Migrations.Sqlite</c>
/// scaffolds the SQLite migration set into THIS assembly (the second provider set, deployment-distribution §8 /
/// deployment-profile "two migration sets"). The Postgres set stays in NomNomzBot.Infrastructure. At runtime the
/// lite profile binds <c>UseSqlite(...).MigrationsAssembly("NomNomzBot.Migrations.Sqlite")</c> so EF discovers
/// exactly this set; the two sets never collide because they live in separate assemblies.
/// </summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AppDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlite(
            "Data Source=./nomnomz.db",
            sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
        );
        return new AppDbContext(optionsBuilder.Options);
    }
}
