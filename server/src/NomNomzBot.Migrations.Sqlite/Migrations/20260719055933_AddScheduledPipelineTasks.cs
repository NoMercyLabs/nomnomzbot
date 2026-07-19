using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledPipelineTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledPipelineTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: true
                    ),
                    DueAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    VariablesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TriggeredByUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    TriggeredByDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledPipelineTasks", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPipelineTasks_BroadcasterId_DedupeKey",
                table: "ScheduledPipelineTasks",
                columns: new[] { "BroadcasterId", "DedupeKey" },
                unique: true,
                filter: "\"Status\" = 'pending'"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPipelineTasks_Status_DueAt",
                table: "ScheduledPipelineTasks",
                columns: new[] { "Status", "DueAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduledPipelineTasks");
        }
    }
}
