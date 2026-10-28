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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddBindingTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Timers",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Timers",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Timers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "ScheduledPipelineTasks",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "ScheduledPipelineTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Rewards",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Rewards",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Rewards",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Rewards",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "Giveaways",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "Giveaways",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "Giveaways",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "Giveaways",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "EventResponses",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "EventResponses",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "EventResponses",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "ChatTriggers",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "ChatTriggers",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "ChatTriggers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionParamsJson",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "TargetActionType",
                table: "CatalogItems",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCommandId",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetKind",
                table: "CatalogItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1
            );

            migrationBuilder.AddColumn<string>(
                name: "AllowedTargetsJson",
                table: "AutomationApiTokens",
                type: "TEXT",
                nullable: true
            );

            // Copy each token's pipeline allow-list in place as pipeline targets; the old list stays readable.
            migrationBuilder.Sql(
                """
                UPDATE "AutomationApiTokens"
                SET "AllowedTargetsJson" = (
                    SELECT json_group_array(json_object('kind', 'pipeline', 'id', value))
                    FROM json_each("AllowedPipelineIdsJson"))
                WHERE "AllowedPipelineIdsJson" IS NOT NULL AND length(trim("AllowedPipelineIdsJson")) > 2;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "Timers");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "Timers");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "Timers");

            migrationBuilder.DropColumn(name: "TargetKind", table: "Timers");

            migrationBuilder.DropColumn(
                name: "TargetActionParamsJson",
                table: "ScheduledPipelineTasks"
            );

            migrationBuilder.DropColumn(name: "TargetActionType", table: "ScheduledPipelineTasks");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "ScheduledPipelineTasks");

            migrationBuilder.DropColumn(name: "TargetKind", table: "ScheduledPipelineTasks");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "Rewards");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "Rewards");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "Rewards");

            migrationBuilder.DropColumn(name: "TargetKind", table: "Rewards");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "Giveaways");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "Giveaways");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "Giveaways");

            migrationBuilder.DropColumn(name: "TargetKind", table: "Giveaways");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "EventResponses");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "EventResponses");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "EventResponses");

            migrationBuilder.DropColumn(name: "TargetKind", table: "EventResponses");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "ChatTriggers");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "ChatTriggers");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "ChatTriggers");

            migrationBuilder.DropColumn(name: "TargetKind", table: "ChatTriggers");

            migrationBuilder.DropColumn(name: "TargetActionParamsJson", table: "CatalogItems");

            migrationBuilder.DropColumn(name: "TargetActionType", table: "CatalogItems");

            migrationBuilder.DropColumn(name: "TargetCommandId", table: "CatalogItems");

            migrationBuilder.DropColumn(name: "TargetKind", table: "CatalogItems");

            migrationBuilder.DropColumn(name: "AllowedTargetsJson", table: "AutomationApiTokens");
        }
    }
}
