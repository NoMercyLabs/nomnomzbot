using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class UserIdentityPartialUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserIdentity_Provider_ProviderUserId",
                table: "UserIdentities"
            );

            migrationBuilder.DropIndex(
                name: "IX_UserIdentity_UserId_Provider",
                table: "UserIdentities"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentity_Provider_ProviderUserId",
                table: "UserIdentities",
                columns: new[] { "Provider", "ProviderUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentity_UserId_Provider",
                table: "UserIdentities",
                columns: new[] { "UserId", "Provider" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserIdentity_Provider_ProviderUserId",
                table: "UserIdentities"
            );

            migrationBuilder.DropIndex(
                name: "IX_UserIdentity_UserId_Provider",
                table: "UserIdentities"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentity_Provider_ProviderUserId",
                table: "UserIdentities",
                columns: new[] { "Provider", "ProviderUserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentity_UserId_Provider",
                table: "UserIdentities",
                columns: new[] { "UserId", "Provider" },
                unique: true
            );
        }
    }
}
