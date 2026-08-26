using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineRunState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineRunStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SuspendedAtStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VariablesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CursorJson = table.Column<string>(type: "TEXT", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggeredByDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    AccumulatedRuntimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    SuspendedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ResumedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRunStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineRunStates_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunStates_PipelineId",
                table: "PipelineRunStates",
                column: "PipelineId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PipelineRunStates");
        }
    }
}
