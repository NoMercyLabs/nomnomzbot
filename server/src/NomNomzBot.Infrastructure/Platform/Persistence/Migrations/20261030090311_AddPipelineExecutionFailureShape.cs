// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineExecutionFailureShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureKind",
                table: "PipelineExecutions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LimitKey",
                table: "PipelineExecutions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "LimitValue",
                table: "PipelineExecutions",
                type: "bigint",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FailureKind", table: "PipelineExecutions");

            migrationBuilder.DropColumn(name: "LimitKey", table: "PipelineExecutions");

            migrationBuilder.DropColumn(name: "LimitValue", table: "PipelineExecutions");
        }
    }
}
