using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
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
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40
            );

            migrationBuilder.AddColumn<string>(
                name: "GroupOp",
                table: "PipelineStepConditions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ParentConditionId",
                table: "PipelineStepConditions",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PipelineTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ConfigJson = table.Column<string>(
                        type: "text",
                        nullable: false,
                        defaultValue: "{}"
                    ),
                    IsEnabled = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true
                    ),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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
                name: "FK_PipelineStepConditions_PipelineStepConditions_ParentConditi~",
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
                name: "FK_PipelineStepConditions_PipelineStepConditions_ParentConditi~",
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
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: ""
            );
        }
    }
}
