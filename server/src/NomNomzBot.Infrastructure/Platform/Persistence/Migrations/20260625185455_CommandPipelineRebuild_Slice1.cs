using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommandPipelineRebuild_Slice1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers"
            );

            migrationBuilder.DropIndex(name: "IX_Timers_BroadcasterId", table: "Timers");

            migrationBuilder.DropIndex(name: "IX_PipelineSteps_PipelineId", table: "PipelineSteps");

            migrationBuilder.DropIndex(name: "IX_Pipelines_BroadcasterId", table: "Pipelines");

            migrationBuilder.DropIndex(
                name: "IX_PipelineExecutions_PipelineId",
                table: "PipelineExecutions"
            );

            migrationBuilder.DropIndex(
                name: "IX_NamedCounters_BroadcasterId",
                table: "NamedCounters"
            );

            migrationBuilder.DropIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses"
            );

            migrationBuilder.DropIndex(name: "IX_CommandUsages_CommandId", table: "CommandUsages");

            migrationBuilder.DropIndex(
                name: "IX_CommandCooldownStates_CommandId",
                table: "CommandCooldownStates"
            );

            migrationBuilder.DropIndex(
                name: "IX_ChannelBuiltinCommands_BroadcasterId",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.RenameIndex(
                name: "IX_PipelineStepConditions_PipelineStepId",
                table: "PipelineStepConditions",
                newName: "IX_PipelineStepCondition_StepId"
            );

            migrationBuilder.AlterDatabase().OldAnnotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Timers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100
            );

            migrationBuilder.AlterColumn<int>(
                name: "MinChatActivity",
                table: "Timers",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<string>(
                name: "Messages",
                table: "Timers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(List<string>),
                oldType: "text[]"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Timers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<int>(
                name: "IntervalMinutes",
                table: "Timers",
                type: "integer",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Timers",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId1",
                table: "Timers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "PipelineSteps",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "PipelineSteps",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<string>(
                name: "ConfigJson",
                table: "PipelineSteps",
                type: "text",
                nullable: false,
                defaultValue: "{}",
                oldClrType: typeof(string),
                oldType: "text"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "Negate",
                table: "PipelineStepConditions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<string>(
                name: "TriggerKind",
                table: "Pipelines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "manual",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30
            );

            migrationBuilder.AlterColumn<long>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint"
            );

            migrationBuilder.AlterColumn<int>(
                name: "MaxStepCount",
                table: "Pipelines",
                type: "integer",
                nullable: false,
                defaultValue: 50,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Pipelines",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<string>(
                name: "TriggerKind",
                table: "PipelineExecutions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30
            );

            migrationBuilder.AlterColumn<int>(
                name: "HostCallCount",
                table: "PipelineExecutions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "PipelineExecutions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "DurationMs",
                table: "PipelineExecutions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<long>(
                name: "Value",
                table: "NamedCounters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint"
            );

            migrationBuilder.AlterColumn<string>(
                name: "ResponseType",
                table: "EventResponses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "chat_message",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50
            );

            migrationBuilder.AlterColumn<Dictionary<string, string>>(
                name: "MetadataJson",
                table: "EventResponses",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(Dictionary<string, string>),
                oldType: "hstore"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "EventResponses",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "EventResponses",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "EventResponses",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "ChannelBuiltinCommands",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean"
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "ChannelBuiltinCommands",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timer_BroadcasterId_IsEnabled",
                table: "Timers",
                columns: new[] { "BroadcasterId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers",
                column: "PipelineId1"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStep_BroadcasterId",
                table: "PipelineSteps",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStep_PipelineId_Order",
                table: "PipelineSteps",
                columns: new[] { "PipelineId", "Order" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pipeline_BroadcasterId_IsEnabled",
                table: "Pipelines",
                columns: new[] { "BroadcasterId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecution_BroadcasterId_StartedAt",
                table: "PipelineExecutions",
                columns: new[] { "BroadcasterId", "StartedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecution_PipelineId_StartedAt",
                table: "PipelineExecutions",
                columns: new[] { "PipelineId", "StartedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NamedCounter_BroadcasterId_Key",
                table: "NamedCounters",
                columns: new[] { "BroadcasterId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventResponse_BroadcasterId_EventType",
                table: "EventResponses",
                columns: new[] { "BroadcasterId", "EventType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsage_BroadcasterId_CreatedAt",
                table: "CommandUsages",
                columns: new[] { "BroadcasterId", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsage_CommandId_CreatedAt",
                table: "CommandUsages",
                columns: new[] { "CommandId", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownState_CommandId_ExpiresAt",
                table: "CommandCooldownStates",
                columns: new[] { "CommandId", "ExpiresAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownState_CommandId_UserId_ExpiresAt",
                table: "CommandCooldownStates",
                columns: new[] { "CommandId", "UserId", "ExpiresAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommand_BroadcasterId_Key",
                table: "ChannelBuiltinCommands",
                columns: new[] { "BroadcasterId", "BuiltinKey" },
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers",
                column: "PipelineId1",
                principalTable: "Pipelines",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers"
            );

            migrationBuilder.DropIndex(name: "IX_Timer_BroadcasterId_IsEnabled", table: "Timers");

            migrationBuilder.DropIndex(name: "IX_Timers_PipelineId1", table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_PipelineStep_BroadcasterId",
                table: "PipelineSteps"
            );

            migrationBuilder.DropIndex(
                name: "IX_PipelineStep_PipelineId_Order",
                table: "PipelineSteps"
            );

            migrationBuilder.DropIndex(
                name: "IX_Pipeline_BroadcasterId_IsEnabled",
                table: "Pipelines"
            );

            migrationBuilder.DropIndex(
                name: "IX_PipelineExecution_BroadcasterId_StartedAt",
                table: "PipelineExecutions"
            );

            migrationBuilder.DropIndex(
                name: "IX_PipelineExecution_PipelineId_StartedAt",
                table: "PipelineExecutions"
            );

            migrationBuilder.DropIndex(
                name: "IX_NamedCounter_BroadcasterId_Key",
                table: "NamedCounters"
            );

            migrationBuilder.DropIndex(
                name: "IX_EventResponse_BroadcasterId_EventType",
                table: "EventResponses"
            );

            migrationBuilder.DropIndex(
                name: "IX_CommandUsage_BroadcasterId_CreatedAt",
                table: "CommandUsages"
            );

            migrationBuilder.DropIndex(
                name: "IX_CommandUsage_CommandId_CreatedAt",
                table: "CommandUsages"
            );

            migrationBuilder.DropIndex(
                name: "IX_CommandCooldownState_CommandId_ExpiresAt",
                table: "CommandCooldownStates"
            );

            migrationBuilder.DropIndex(
                name: "IX_CommandCooldownState_CommandId_UserId_ExpiresAt",
                table: "CommandCooldownStates"
            );

            migrationBuilder.DropIndex(
                name: "IX_ChannelBuiltinCommand_BroadcasterId_Key",
                table: "ChannelBuiltinCommands"
            );

            migrationBuilder.DropColumn(name: "PipelineId1", table: "Timers");

            migrationBuilder.RenameIndex(
                name: "IX_PipelineStepCondition_StepId",
                table: "PipelineStepConditions",
                newName: "IX_PipelineStepConditions_PipelineStepId"
            );

            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Timers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200
            );

            migrationBuilder.AlterColumn<int>(
                name: "MinChatActivity",
                table: "Timers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0
            );

            migrationBuilder.AlterColumn<List<string>>(
                name: "Messages",
                table: "Timers",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Timers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "IntervalMinutes",
                table: "Timers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 30
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Timers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "PipelineSteps",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "PipelineSteps",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1
            );

            migrationBuilder.AlterColumn<string>(
                name: "ConfigJson",
                table: "PipelineSteps",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "{}"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "Negate",
                table: "PipelineStepConditions",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false
            );

            migrationBuilder.AlterColumn<string>(
                name: "TriggerKind",
                table: "Pipelines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "manual"
            );

            migrationBuilder.AlterColumn<long>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L
            );

            migrationBuilder.AlterColumn<int>(
                name: "MaxStepCount",
                table: "Pipelines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 50
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Pipelines",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "TriggerKind",
                table: "PipelineExecutions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40
            );

            migrationBuilder.AlterColumn<int>(
                name: "HostCallCount",
                table: "PipelineExecutions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0
            );

            migrationBuilder.AlterColumn<string>(
                name: "ErrorMessage",
                table: "PipelineExecutions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "DurationMs",
                table: "PipelineExecutions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0
            );

            migrationBuilder.AlterColumn<long>(
                name: "Value",
                table: "NamedCounters",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L
            );

            migrationBuilder.AlterColumn<string>(
                name: "ResponseType",
                table: "EventResponses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "chat_message"
            );

            migrationBuilder.AlterColumn<Dictionary<string, string>>(
                name: "MetadataJson",
                table: "EventResponses",
                type: "hstore",
                nullable: false,
                oldClrType: typeof(Dictionary<string, string>),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "EventResponses",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "EventResponses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "EventResponses",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "ChannelBuiltinCommands",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "ConfigSchemaVersion",
                table: "ChannelBuiltinCommands",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timers_BroadcasterId",
                table: "Timers",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineSteps_PipelineId",
                table: "PipelineSteps",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_BroadcasterId",
                table: "Pipelines",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecutions_PipelineId",
                table: "PipelineExecutions",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NamedCounters_BroadcasterId",
                table: "NamedCounters",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsages_CommandId",
                table: "CommandUsages",
                column: "CommandId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownStates_CommandId",
                table: "CommandCooldownStates",
                column: "CommandId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommands_BroadcasterId",
                table: "ChannelBuiltinCommands",
                column: "BroadcasterId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id"
            );
        }
    }
}
