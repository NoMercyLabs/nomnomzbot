using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineTreeModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ConditionType",
                table: "PipelineStepConditions",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 40
            );

            migrationBuilder.AddColumn<string>(
                name: "GroupOp",
                table: "PipelineStepConditions",
                type: "TEXT",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ParentConditionId",
                table: "PipelineStepConditions",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PipelineTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigJson = table.Column<string>(
                        type: "TEXT",
                        nullable: false,
                        defaultValue: "{}"
                    ),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineTriggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineTriggers_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStepCondition_ParentConditionId",
                table: "PipelineStepConditions",
                column: "ParentConditionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineTrigger_BroadcasterId",
                table: "PipelineTriggers",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineTrigger_PipelineId_Order",
                table: "PipelineTriggers",
                columns: new[] { "PipelineId", "Order" },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_PipelineStepConditions_PipelineStepConditions_ParentConditionId",
                table: "PipelineStepConditions",
                column: "ParentConditionId",
                principalTable: "PipelineStepConditions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PipelineStepConditions_PipelineStepConditions_ParentConditionId",
                table: "PipelineStepConditions"
            );

            migrationBuilder.DropTable(name: "PipelineTriggers");

            migrationBuilder.DropIndex(
                name: "IX_PipelineStepCondition_ParentConditionId",
                table: "PipelineStepConditions"
            );

            migrationBuilder.DropColumn(name: "GroupOp", table: "PipelineStepConditions");

            migrationBuilder.DropColumn(name: "ParentConditionId", table: "PipelineStepConditions");

            migrationBuilder.AlterColumn<string>(
                name: "ConditionType",
                table: "PipelineStepConditions",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 40,
                oldDefaultValue: ""
            );
        }
    }
}
