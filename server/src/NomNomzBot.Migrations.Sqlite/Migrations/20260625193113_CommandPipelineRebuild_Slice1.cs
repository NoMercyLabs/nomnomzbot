using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CommandPipelineRebuild_Slice1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timers_BroadcasterId",
                table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_Pipelines_BroadcasterId",
                table: "Pipelines");

            migrationBuilder.DropIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses");

            migrationBuilder.DropIndex(
                name: "IX_Command_Name_BroadcasterId",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "GraphJson",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "Permission",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "Responses",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Commands");

            migrationBuilder.RenameColumn(
                name: "PipelineJson",
                table: "EventResponses",
                newName: "PipelineId");

            migrationBuilder.RenameColumn(
                name: "Metadata",
                table: "EventResponses",
                newName: "MetadataJson");

            migrationBuilder.RenameColumn(
                name: "Response",
                table: "Commands",
                newName: "TemplateResponse");

            migrationBuilder.RenameColumn(
                name: "PipelineJson",
                table: "Commands",
                newName: "TemplateResponses");

            migrationBuilder.AlterColumn<int>(
                name: "MinChatActivity",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "IntervalMinutes",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Timers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "Timers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId1",
                table: "Timers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Pipelines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GraphJsonCache",
                table: "Pipelines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxStepCount",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<string>(
                name: "TriggerKind",
                table: "Pipelines",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AlterColumn<string>(
                name: "ResponseType",
                table: "EventResponses",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "chat_message",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EventResponses",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "EventResponses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Commands",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CustomPrefix",
                table: "Commands",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "Commands",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchMode",
                table: "Commands",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MatchPattern",
                table: "Commands",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinPermissionLevel",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Commands",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrefixMode",
                table: "Commands",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Commands",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "template");

            migrationBuilder.AddColumn<long>(
                name: "UseCount",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "UserCooldownSeconds",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChannelBuiltinCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuiltinKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    OverridesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBuiltinCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelBuiltinCommands_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommandCooldownStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastInvokedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandCooldownStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandCooldownStates_Commands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "Commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommandUsages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CommandNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ViewerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArgsSnapshot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    WasSuccessful = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandUsages_Commands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "Commands",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NamedCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamedCounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NamedCounters_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TriggerKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HostCallCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StepLogsJson = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineExecutions_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentStepId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    CodeScriptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineSteps_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineStepConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PipelineStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConditionType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LeftOperand = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RightOperand = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Negate = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineStepConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineStepConditions_PipelineSteps_PipelineStepId",
                        column: x => x.PipelineStepId,
                        principalTable: "PipelineSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Timer_BroadcasterId_IsEnabled",
                table: "Timers",
                columns: new[] { "BroadcasterId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId",
                table: "Timers",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers",
                column: "PipelineId1");

            migrationBuilder.CreateIndex(
                name: "IX_Pipeline_BroadcasterId_IsEnabled",
                table: "Pipelines",
                columns: new[] { "BroadcasterId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_EventResponse_BroadcasterId_EventType",
                table: "EventResponses",
                columns: new[] { "BroadcasterId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_PipelineId",
                table: "EventResponses",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands",
                columns: new[] { "NameNormalized", "BroadcasterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommand_BroadcasterId_Key",
                table: "ChannelBuiltinCommands",
                columns: new[] { "BroadcasterId", "BuiltinKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownState_CommandId_ExpiresAt",
                table: "CommandCooldownStates",
                columns: new[] { "CommandId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownState_CommandId_UserId_ExpiresAt",
                table: "CommandCooldownStates",
                columns: new[] { "CommandId", "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsage_BroadcasterId_CreatedAt",
                table: "CommandUsages",
                columns: new[] { "BroadcasterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsage_CommandId_CreatedAt",
                table: "CommandUsages",
                columns: new[] { "CommandId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NamedCounter_BroadcasterId_Key",
                table: "NamedCounters",
                columns: new[] { "BroadcasterId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecution_BroadcasterId_StartedAt",
                table: "PipelineExecutions",
                columns: new[] { "BroadcasterId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecution_PipelineId_StartedAt",
                table: "PipelineExecutions",
                columns: new[] { "PipelineId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStepCondition_StepId",
                table: "PipelineStepConditions",
                column: "PipelineStepId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStep_BroadcasterId",
                table: "PipelineSteps",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStep_PipelineId_Order",
                table: "PipelineSteps",
                columns: new[] { "PipelineId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers",
                column: "PipelineId",
                principalTable: "Pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers",
                column: "PipelineId1",
                principalTable: "Pipelines",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventResponses_Pipelines_PipelineId",
                table: "EventResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId",
                table: "Timers");

            migrationBuilder.DropForeignKey(
                name: "FK_Timers_Pipelines_PipelineId1",
                table: "Timers");

            migrationBuilder.DropTable(
                name: "ChannelBuiltinCommands");

            migrationBuilder.DropTable(
                name: "CommandCooldownStates");

            migrationBuilder.DropTable(
                name: "CommandUsages");

            migrationBuilder.DropTable(
                name: "NamedCounters");

            migrationBuilder.DropTable(
                name: "PipelineExecutions");

            migrationBuilder.DropTable(
                name: "PipelineStepConditions");

            migrationBuilder.DropTable(
                name: "PipelineSteps");

            migrationBuilder.DropIndex(
                name: "IX_Timer_BroadcasterId_IsEnabled",
                table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_Timers_PipelineId",
                table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_Timers_PipelineId1",
                table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_Pipeline_BroadcasterId_IsEnabled",
                table: "Pipelines");

            migrationBuilder.DropIndex(
                name: "IX_EventResponse_BroadcasterId_EventType",
                table: "EventResponses");

            migrationBuilder.DropIndex(
                name: "IX_EventResponses_PipelineId",
                table: "EventResponses");

            migrationBuilder.DropIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "ConfigSchemaVersion",
                table: "Timers");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "Timers");

            migrationBuilder.DropColumn(
                name: "PipelineId1",
                table: "Timers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "GraphJsonCache",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "MaxStepCount",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "TriggerKind",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "ConfigSchemaVersion",
                table: "EventResponses");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "EventResponses");

            migrationBuilder.DropColumn(
                name: "ConfigSchemaVersion",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "CustomPrefix",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "MatchMode",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "MatchPattern",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "MinPermissionLevel",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "PrefixMode",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "UseCount",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "UserCooldownSeconds",
                table: "Commands");

            migrationBuilder.RenameColumn(
                name: "PipelineId",
                table: "EventResponses",
                newName: "PipelineJson");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "EventResponses",
                newName: "Metadata");

            migrationBuilder.RenameColumn(
                name: "TemplateResponses",
                table: "Commands",
                newName: "PipelineJson");

            migrationBuilder.RenameColumn(
                name: "TemplateResponse",
                table: "Commands",
                newName: "Response");

            migrationBuilder.AlterColumn<int>(
                name: "MinChatActivity",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "IntervalMinutes",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 30);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Timers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AlterColumn<int>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Pipelines",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "GraphJson",
                table: "Pipelines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "ResponseType",
                table: "EventResponses",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 40,
                oldDefaultValue: "chat_message");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "EventResponses",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AlterColumn<int>(
                name: "PipelineId",
                table: "Commands",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Commands",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "Permission",
                table: "Commands",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "everyone");

            migrationBuilder.AddColumn<string>(
                name: "Responses",
                table: "Commands",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Commands",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Timers_BroadcasterId",
                table: "Timers",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_BroadcasterId",
                table: "Pipelines",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses",
                column: "BroadcasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Command_Name_BroadcasterId",
                table: "Commands",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true);
        }
    }
}
