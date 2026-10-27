using Microsoft.EntityFrameworkCore.Migrations;
using NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewriteRetiredTemplateKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only. The registry no longer accepts the short character keys ({space} and the like), so
            // every saved template moves to the canonical key ({chars.space}) once. Arguments after ':' and
            // escaped braces stay as they are. The same statements run in the SQLite set.
            foreach (
                string statement in RetiredTemplateKeySql.Statements(TemplateSqlDialect.Postgres)
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
