using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Redemptions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RedemptionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    RewardId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RewardTitle = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    Cost = table.Column<int>(type: "INTEGER", nullable: false),
                    UserInput = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redemptions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Redemptions_BroadcasterId_RedemptionId",
                table: "Redemptions",
                columns: new[] { "BroadcasterId", "RedemptionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Redemptions_BroadcasterId_Status_RedeemedAt",
                table: "Redemptions",
                columns: new[] { "BroadcasterId", "Status", "RedeemedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Redemptions");
        }
    }
}
