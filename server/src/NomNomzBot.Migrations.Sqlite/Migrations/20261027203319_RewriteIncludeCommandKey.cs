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
