using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePipelineExecutionPipelineIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PipelineExecutions_Pipelines_PipelineId",
                table: "PipelineExecutions"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineExecutions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_PipelineExecutions_Pipelines_PipelineId",
                table: "PipelineExecutions",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PipelineExecutions_Pipelines_PipelineId",
                table: "PipelineExecutions"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "PipelineExecutions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_PipelineExecutions_Pipelines_PipelineId",
                table: "PipelineExecutions",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }
    }
}
