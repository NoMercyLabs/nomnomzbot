using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultTimeoutSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultTimeoutSeconds",
                table: "ModerationEscalationPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 600
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTimeoutSeconds",
                table: "ModerationEscalationPolicies"
            );
        }
    }
}
