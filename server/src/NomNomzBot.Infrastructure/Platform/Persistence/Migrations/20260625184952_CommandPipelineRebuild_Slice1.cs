using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NomNomzBot.Infrastructure.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommandPipelineRebuild_Slice1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Command_Name_BroadcasterId", table: "Commands");

            migrationBuilder.DropColumn(name: "GraphJson", table: "Pipelines");

            migrationBuilder.DropColumn(name: "PipelineJson", table: "EventResponses");

            migrationBuilder.DropColumn(name: "Permission", table: "Commands");

            migrationBuilder.DropColumn(name: "PipelineJson", table: "Commands");

            migrationBuilder.DropColumn(name: "Responses", table: "Commands");

            migrationBuilder.DropColumn(name: "Type", table: "Commands");

            migrationBuilder.RenameColumn(
                name: "Metadata",
                table: "EventResponses",
                newName: "MetadataJson"
            );

            migrationBuilder.RenameColumn(
                name: "Response",
                table: "Commands",
                newName: "TemplateResponse"
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "Id",
                    table: "Timers",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(int),
                    oldType: "integer"
                )
                .OldAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Timers",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "Timers",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AlterColumn<long>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "Id",
                    table: "Pipelines",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(int),
                    oldType: "integer"
                )
                .OldAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Pipelines",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "GraphJsonCache",
                table: "Pipelines",
                type: "text",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MaxStepCount",
                table: "Pipelines",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "TriggerKind",
                table: "Pipelines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "Id",
                    table: "EventResponses",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(int),
                    oldType: "integer"
                )
                .OldAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "EventResponses",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "EventResponses",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "EventResponses",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "PipelineId",
                table: "Commands",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true
            );

            migrationBuilder
                .AlterColumn<Guid>(
                    name: "Id",
                    table: "Commands",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(int),
                    oldType: "integer"
                )
                .OldAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<int>(
                name: "ConfigSchemaVersion",
                table: "Commands",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "CustomPrefix",
                table: "Commands",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "Commands",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "MatchMode",
                table: "Commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "MatchPattern",
                table: "Commands",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MinPermissionLevel",
                table: "Commands",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Commands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "PrefixMode",
                table: "Commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "TemplateResponses",
                table: "Commands",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'[]'::jsonb"
            );

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "Commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "template"
            );

            migrationBuilder.AddColumn<long>(
                name: "UseCount",
                table: "Commands",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<int>(
                name: "UserCooldownSeconds",
                table: "Commands",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "ChannelBuiltinCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuiltinKey = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OverridesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBuiltinCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelBuiltinCommands_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "CommandCooldownStates",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastInvokedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ExpiresAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
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
                    table.PrimaryKey("PK_CommandCooldownStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandCooldownStates_Commands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "Commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "CommandUsages",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandNameSnapshot = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    ViewerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArgsSnapshot = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    WasSuccessful = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_CommandUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandUsages_Commands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "Commands",
                        principalColumn: "Id"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "NamedCounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    DeletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamedCounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NamedCounters_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "PipelineExecutions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerKind = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    HostCallCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(
                        type: "character varying(1000)",
                        maxLength: 1000,
                        nullable: true
                    ),
                    StepLogsJson = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: true
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
                    table.PrimaryKey("PK_PipelineExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineExecutions_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "PipelineSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentStepId = table.Column<Guid>(type: "uuid", nullable: true),
                    Branch = table.Column<string>(
                        type: "character varying(10)",
                        maxLength: 10,
                        nullable: true
                    ),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(
                        type: "character varying(60)",
                        maxLength: 60,
                        nullable: false
                    ),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    ConfigSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CodeScriptId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_PipelineSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineSteps_Pipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "Pipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "PipelineStepConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConditionType = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    Operator = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    LeftOperand = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    RightOperand = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    Negate = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PipelineStepConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineStepConditions_PipelineSteps_PipelineStepId",
                        column: x => x.PipelineStepId,
                        principalTable: "PipelineSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timers_PipelineId",
                table: "Timers",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_PipelineId",
                table: "EventResponses",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands",
                columns: new[] { "NameNormalized", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBuiltinCommands_BroadcasterId",
                table: "ChannelBuiltinCommands",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandCooldownStates_CommandId",
                table: "CommandCooldownStates",
                column: "CommandId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CommandUsages_CommandId",
                table: "CommandUsages",
                column: "CommandId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NamedCounters_BroadcasterId",
                table: "NamedCounters",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineExecutions_PipelineId",
                table: "PipelineExecutions",
                column: "PipelineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStepConditions_PipelineStepId",
                table: "PipelineStepConditions",
                column: "PipelineStepId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PipelineSteps_PipelineId",
                table: "PipelineSteps",
                column: "PipelineId"
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

            migrationBuilder.DropTable(name: "ChannelBuiltinCommands");

            migrationBuilder.DropTable(name: "CommandCooldownStates");

            migrationBuilder.DropTable(name: "CommandUsages");

            migrationBuilder.DropTable(name: "NamedCounters");

            migrationBuilder.DropTable(name: "PipelineExecutions");

            migrationBuilder.DropTable(name: "PipelineStepConditions");

            migrationBuilder.DropTable(name: "PipelineSteps");

            migrationBuilder.DropIndex(name: "IX_Timers_PipelineId", table: "Timers");

            migrationBuilder.DropIndex(
                name: "IX_EventResponses_PipelineId",
                table: "EventResponses"
            );

            migrationBuilder.DropIndex(
                name: "IX_Command_NameNormalized_BroadcasterId",
                table: "Commands"
            );

            migrationBuilder.DropColumn(name: "ConfigSchemaVersion", table: "Timers");

            migrationBuilder.DropColumn(name: "PipelineId", table: "Timers");

            migrationBuilder.DropColumn(name: "DeletedAt", table: "Pipelines");

            migrationBuilder.DropColumn(name: "GraphJsonCache", table: "Pipelines");

            migrationBuilder.DropColumn(name: "MaxStepCount", table: "Pipelines");

            migrationBuilder.DropColumn(name: "TriggerKind", table: "Pipelines");

            migrationBuilder.DropColumn(name: "ConfigSchemaVersion", table: "EventResponses");

            migrationBuilder.DropColumn(name: "DeletedAt", table: "EventResponses");

            migrationBuilder.DropColumn(name: "PipelineId", table: "EventResponses");

            migrationBuilder.DropColumn(name: "ConfigSchemaVersion", table: "Commands");

            migrationBuilder.DropColumn(name: "CustomPrefix", table: "Commands");

            migrationBuilder.DropColumn(name: "LastUsedAt", table: "Commands");

            migrationBuilder.DropColumn(name: "MatchMode", table: "Commands");

            migrationBuilder.DropColumn(name: "MatchPattern", table: "Commands");

            migrationBuilder.DropColumn(name: "MinPermissionLevel", table: "Commands");

            migrationBuilder.DropColumn(name: "NameNormalized", table: "Commands");

            migrationBuilder.DropColumn(name: "PrefixMode", table: "Commands");

            migrationBuilder.DropColumn(name: "TemplateResponses", table: "Commands");

            migrationBuilder.DropColumn(name: "Tier", table: "Commands");

            migrationBuilder.DropColumn(name: "UseCount", table: "Commands");

            migrationBuilder.DropColumn(name: "UserCooldownSeconds", table: "Commands");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "EventResponses",
                newName: "Metadata"
            );

            migrationBuilder.RenameColumn(
                name: "TemplateResponse",
                table: "Commands",
                newName: "Response"
            );

            migrationBuilder
                .AlterColumn<int>(
                    name: "Id",
                    table: "Timers",
                    type: "integer",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid"
                )
                .Annotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AlterColumn<int>(
                name: "TriggerCount",
                table: "Pipelines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint"
            );

            migrationBuilder
                .AlterColumn<int>(
                    name: "Id",
                    table: "Pipelines",
                    type: "integer",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid"
                )
                .Annotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<string>(
                name: "GraphJson",
                table: "Pipelines",
                type: "text",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder
                .AlterColumn<int>(
                    name: "Id",
                    table: "EventResponses",
                    type: "integer",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid"
                )
                .Annotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<string>(
                name: "PipelineJson",
                table: "EventResponses",
                type: "text",
                nullable: true
            );

            migrationBuilder.AlterColumn<int>(
                name: "PipelineId",
                table: "Commands",
                type: "integer",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true
            );

            migrationBuilder
                .AlterColumn<int>(
                    name: "Id",
                    table: "Commands",
                    type: "integer",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid"
                )
                .Annotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                );

            migrationBuilder.AddColumn<string>(
                name: "Permission",
                table: "Commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "everyone"
            );

            migrationBuilder.AddColumn<string>(
                name: "PipelineJson",
                table: "Commands",
                type: "jsonb",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Responses",
                table: "Commands",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb"
            );

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Commands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "text"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_Name_BroadcasterId",
                table: "Commands",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true
            );
        }
    }
}
