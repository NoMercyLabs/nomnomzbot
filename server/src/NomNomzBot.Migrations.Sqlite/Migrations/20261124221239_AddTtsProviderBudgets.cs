using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsProviderBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FallbackReason",
                table: "TtsUsageRecords",
                type: "TEXT",
                maxLength: 30,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Share",
                table: "TtsUsageRecords",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "default"
            );

            migrationBuilder.AddColumn<string>(
                name: "FallbackProvidersJson",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderBudgetsJson",
                table: "TtsConfigs",
                type: "TEXT",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "BudgetShare",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                maxLength: 80,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProviderOverride",
                table: "TtsApprovalQueueEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_Provider_Share_OccurredAt",
                table: "TtsUsageRecords",
                columns: new[] { "BroadcasterId", "Provider", "Share", "OccurredAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TtsUsageRecords_BroadcasterId_Provider_Share_OccurredAt",
                table: "TtsUsageRecords"
            );

            migrationBuilder.DropColumn(name: "FallbackReason", table: "TtsUsageRecords");

            migrationBuilder.DropColumn(name: "Share", table: "TtsUsageRecords");

            migrationBuilder.DropColumn(name: "FallbackProvidersJson", table: "TtsConfigs");

            migrationBuilder.DropColumn(name: "ProviderBudgetsJson", table: "TtsConfigs");

            migrationBuilder.DropColumn(name: "BudgetShare", table: "TtsApprovalQueueEntries");

            migrationBuilder.DropColumn(name: "ProviderOverride", table: "TtsApprovalQueueEntries");
        }
    }
}
