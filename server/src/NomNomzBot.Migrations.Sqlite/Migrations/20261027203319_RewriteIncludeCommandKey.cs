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

using Microsoft.EntityFrameworkCore.Migrations;
using NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RewriteIncludeCommandKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only. Including another command is {include.cmd:name} now ({command.*} is the running command),
            // so every saved {include.command:name} moves to the new key once. Arguments after ':' and escaped
            // braces stay as they are. The same statements run in the Postgres set.
            foreach (
                string statement in RetiredTemplateKeySql.Statements(
                    TemplateSqlDialect.Sqlite,
                    RetiredIncludeKeyMap.Pairs
                )
            )
                migrationBuilder.Sql(statement);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix — irreversible by design, like every other backfill migration here.
        }
    }
}
