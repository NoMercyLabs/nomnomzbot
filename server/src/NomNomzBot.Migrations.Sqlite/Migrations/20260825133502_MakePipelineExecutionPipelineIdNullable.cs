using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT"
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
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
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
