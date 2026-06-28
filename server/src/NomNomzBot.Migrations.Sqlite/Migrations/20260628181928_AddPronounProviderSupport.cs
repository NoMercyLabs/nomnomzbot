using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPronounProviderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AltPronounId",
                table: "Users",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Pronouns",
                type: "TEXT",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Users_AltPronounId",
                table: "Users",
                column: "AltPronounId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pronouns_Key",
                table: "Pronouns",
                column: "Key",
                unique: true,
                filter: "\"Key\" IS NOT NULL"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Pronouns_AltPronounId",
                table: "Users",
                column: "AltPronounId",
                principalTable: "Pronouns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Users_Pronouns_AltPronounId", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Users_AltPronounId", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Pronouns_Key", table: "Pronouns");

            migrationBuilder.DropColumn(name: "AltPronounId", table: "Users");

            migrationBuilder.DropColumn(name: "Key", table: "Pronouns");
        }
    }
}
