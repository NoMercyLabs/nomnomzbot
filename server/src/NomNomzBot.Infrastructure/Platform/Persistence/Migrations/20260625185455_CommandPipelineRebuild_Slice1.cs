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
            // ── Pipelines table: integer Id → uuid Id ─────────────────────────────────────────────────
            // The Initial migration created Pipelines.Id as integer (a schema/entity mismatch — the
            // domain entity uses Guid). The deleted dev-only migration converted it to uuid. Without
            // that migration, Slice1's AddForeignKey (PipelineId uuid → Pipelines.Id) fails with 42804.
            // On a fresh install Pipelines has no rows, so we can safely drop and recreate the table.
            // On an existing dev DB where the deleted migration already ran (Pipelines.Id is uuid),
            // the DO block is a no-op.
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='Pipelines' AND column_name='Id')
                       = 'integer' THEN
                        -- Commands.PipelineId is integer FK → Pipelines.Id; must be converted to uuid.
                        -- Drop the FK and index first (both depend on Pipelines), then cascade-drop the table.
                        ALTER TABLE ""Commands"" DROP CONSTRAINT IF EXISTS ""FK_Commands_Pipelines_PipelineId"";
                        DROP INDEX IF EXISTS ""IX_Command_PipelineId"";
                        DROP TABLE ""Pipelines"" CASCADE;
                        CREATE TABLE ""Pipelines"" (
                            ""Id""               uuid NOT NULL,
                            ""BroadcasterId""    uuid NOT NULL,
                            ""CreatedAt""        timestamp with time zone NOT NULL,
                            ""DeletedAt""        timestamp with time zone,
                            ""Description""      character varying(500),
                            ""GraphJsonCache""   text,
                            ""IsEnabled""        boolean NOT NULL,
                            ""LastTriggeredAt""  timestamp with time zone,
                            ""MaxStepCount""     integer NOT NULL DEFAULT 0,
                            ""Name""             character varying(200) NOT NULL,
                            ""TriggerCount""     bigint NOT NULL DEFAULT 0,
                            ""TriggerKind""      character varying(30) NOT NULL DEFAULT 'manual',
                            ""UpdatedAt""        timestamp with time zone NOT NULL,
                            CONSTRAINT ""PK_Pipelines"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_Pipelines_Channels_BroadcasterId"" FOREIGN KEY (""BroadcasterId"")
                                REFERENCES ""Channels"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_Pipelines_BroadcasterId"" ON ""Pipelines"" (""BroadcasterId"");
                        -- Convert Commands.PipelineId from integer to uuid (table has no data yet).
                        ALTER TABLE ""Commands"" ALTER COLUMN ""PipelineId"" TYPE uuid USING NULL::uuid;
                        CREATE INDEX ""IX_Command_PipelineId"" ON ""Commands"" (""PipelineId"");
                        ALTER TABLE ""Commands"" ADD CONSTRAINT ""FK_Commands_Pipelines_PipelineId""
                            FOREIGN KEY (""PipelineId"") REFERENCES ""Pipelines"" (""Id"") ON DELETE SET NULL;
                    END IF;
                END $$;"
            );

            // ── Commands table: integer Id → uuid Id ─────────────────────────────────────────────────
            // Same schema/entity mismatch as Pipelines — Commands.Id was integer in the old Initial.
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='Commands' AND column_name='Id')
                       = 'integer' THEN
                        DROP TABLE ""Commands"" CASCADE;
                        CREATE TABLE ""Commands"" (
                            ""Id""              uuid NOT NULL,
                            ""BroadcasterId""   uuid NOT NULL,
                            ""Name""            character varying(100) NOT NULL,
                            ""Permission""      character varying(20) NOT NULL DEFAULT 'everyone',
                            ""Type""            character varying(20) NOT NULL DEFAULT 'text',
                            ""Response""        character varying(2000),
                            ""Responses""       jsonb NOT NULL DEFAULT '[]'::jsonb,
                            ""PipelineJson""    jsonb,
                            ""IsEnabled""       boolean NOT NULL DEFAULT true,
                            ""Description""     character varying(500),
                            ""CooldownSeconds"" integer NOT NULL DEFAULT 0,
                            ""CooldownPerUser"" boolean NOT NULL DEFAULT false,
                            ""Aliases""         jsonb NOT NULL DEFAULT '[]'::jsonb,
                            ""IsPlatform""      boolean NOT NULL DEFAULT false,
                            ""CreatedAt""       timestamp with time zone NOT NULL,
                            ""UpdatedAt""       timestamp with time zone NOT NULL,
                            ""DeletedAt""       timestamp with time zone,
                            CONSTRAINT ""PK_Commands"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_Commands_Channels_BroadcasterId"" FOREIGN KEY (""BroadcasterId"")
                                REFERENCES ""Channels"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_Commands_BroadcasterId"" ON ""Commands"" (""BroadcasterId"");
                    END IF;
                END $$;"
            );

            // ── EventResponses table: integer Id → uuid Id ───────────────────────────────────────────
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='EventResponses' AND column_name='Id')
                       = 'integer' THEN
                        DROP TABLE ""EventResponses"" CASCADE;
                        CREATE TABLE ""EventResponses"" (
                            ""Id""                    uuid NOT NULL,
                            ""BroadcasterId""          uuid NOT NULL,
                            ""EventType""              character varying(100) NOT NULL,
                            ""IsEnabled""              boolean NOT NULL DEFAULT true,
                            ""ResponseType""           character varying(50) NOT NULL DEFAULT 'message',
                            ""Message""                character varying(2000),
                            ""PipelineJson""           text,
                            ""MetadataJson""           jsonb NOT NULL DEFAULT '{}'::jsonb,
                            ""CreatedAt""              timestamp with time zone NOT NULL,
                            ""UpdatedAt""              timestamp with time zone NOT NULL,
                            CONSTRAINT ""PK_EventResponses"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_EventResponses_Channels_BroadcasterId"" FOREIGN KEY (""BroadcasterId"")
                                REFERENCES ""Channels"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_EventResponses_BroadcasterId"" ON ""EventResponses"" (""BroadcasterId"");
                    END IF;
                END $$;"
            );

            // ── Timers table: integer Id → uuid Id ───────────────────────────────────────────────────
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='Timers' AND column_name='Id')
                       = 'integer' THEN
                        DROP TABLE ""Timers"" CASCADE;
                        CREATE TABLE ""Timers"" (
                            ""Id""                uuid NOT NULL,
                            ""BroadcasterId""     uuid NOT NULL,
                            ""Name""              character varying(100) NOT NULL,
                            ""Messages""          jsonb NOT NULL DEFAULT '[]'::jsonb,
                            ""IntervalMinutes""   integer NOT NULL DEFAULT 30,
                            ""MinChatActivity""   integer NOT NULL DEFAULT 0,
                            ""IsEnabled""         boolean NOT NULL DEFAULT true,
                            ""LastFiredAt""       timestamp with time zone,
                            ""NextMessageIndex""  integer NOT NULL DEFAULT 0,
                            ""CreatedAt""         timestamp with time zone NOT NULL,
                            ""UpdatedAt""         timestamp with time zone NOT NULL,
                            ""DeletedAt""         timestamp with time zone,
                            CONSTRAINT ""PK_Timers"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_Timers_Channels_BroadcasterId"" FOREIGN KEY (""BroadcasterId"")
                                REFERENCES ""Channels"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_Timers_BroadcasterId"" ON ""Timers"" (""BroadcasterId"");
                    END IF;
                END $$;"
            );

            // Bootstrap tables that were created in a dev-only migration never committed to the official
            // history. On a fresh install these tables do not exist yet; IF NOT EXISTS makes this safe
            // for both fresh installs and existing databases. The schema here is the PRE-Slice1 baseline
            // so the AlterColumn calls later in this method work correctly on both paths.
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""PipelineSteps"" (
                    ""Id"" uuid NOT NULL,
                    ""ActionType"" character varying(60) NOT NULL,
                    ""Branch"" character varying(10),
                    ""BroadcasterId"" uuid NOT NULL,
                    ""CodeScriptId"" uuid,
                    ""ConfigJson"" text NOT NULL,
                    ""ConfigSchemaVersion"" integer NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""IsEnabled"" boolean NOT NULL,
                    ""Order"" integer NOT NULL,
                    ""ParentStepId"" uuid,
                    ""PipelineId"" uuid NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_PipelineSteps"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_PipelineSteps_PipelineId"" ON ""PipelineSteps"" (""PipelineId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""PipelineStepConditions"" (
                    ""Id"" uuid NOT NULL,
                    ""BroadcasterId"" uuid NOT NULL,
                    ""ConditionType"" character varying(40) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""LeftOperand"" character varying(500),
                    ""Negate"" boolean NOT NULL,
                    ""Operator"" character varying(20),
                    ""Order"" integer NOT NULL,
                    ""PipelineStepId"" uuid NOT NULL,
                    ""RightOperand"" character varying(500),
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_PipelineStepConditions"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_PipelineStepConditions_PipelineStepId"" ON ""PipelineStepConditions"" (""PipelineStepId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""PipelineExecutions"" (
                    ""Id"" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                    ""BroadcasterId"" uuid NOT NULL,
                    ""CompletedAt"" timestamp with time zone,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""DurationMs"" integer NOT NULL,
                    ""ErrorMessage"" character varying(1000),
                    ""HostCallCount"" integer NOT NULL,
                    ""PipelineId"" uuid NOT NULL,
                    ""StartedAt"" timestamp with time zone NOT NULL,
                    ""Status"" character varying(20) NOT NULL,
                    ""StepLogsJson"" text,
                    ""TriggerKind"" character varying(30) NOT NULL,
                    ""TriggeredByUserId"" uuid,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_PipelineExecutions"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_PipelineExecutions_PipelineId"" ON ""PipelineExecutions"" (""PipelineId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""NamedCounters"" (
                    ""Id"" uuid NOT NULL,
                    ""BroadcasterId"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""DeletedAt"" timestamp with time zone,
                    ""Key"" character varying(50) NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""Value"" bigint NOT NULL,
                    CONSTRAINT ""PK_NamedCounters"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_NamedCounters_BroadcasterId"" ON ""NamedCounters"" (""BroadcasterId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""ChannelBuiltinCommands"" (
                    ""Id"" uuid NOT NULL,
                    ""BroadcasterId"" uuid NOT NULL,
                    ""BuiltinKey"" character varying(100) NOT NULL,
                    ""ConfigSchemaVersion"" integer NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""DeletedAt"" timestamp with time zone,
                    ""IsEnabled"" boolean NOT NULL,
                    ""OverridesJson"" text,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_ChannelBuiltinCommands"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_ChannelBuiltinCommands_BroadcasterId"" ON ""ChannelBuiltinCommands"" (""BroadcasterId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""CommandCooldownStates"" (
                    ""Id"" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                    ""BroadcasterId"" uuid NOT NULL,
                    ""CommandId"" uuid NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""ExpiresAt"" timestamp with time zone NOT NULL,
                    ""LastInvokedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""UserId"" uuid,
                    CONSTRAINT ""PK_CommandCooldownStates"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_CommandCooldownStates_CommandId"" ON ""CommandCooldownStates"" (""CommandId"");"
            );
            migrationBuilder.Sql(
                @"
                CREATE TABLE IF NOT EXISTS ""CommandUsages"" (
                    ""Id"" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                    ""ArgsSnapshot"" character varying(500),
                    ""BroadcasterId"" uuid NOT NULL,
                    ""CommandId"" uuid,
                    ""CommandNameSnapshot"" character varying(100) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""ViewerProfileId"" uuid NOT NULL,
                    ""ViewerUserId"" uuid NOT NULL,
                    ""WasSuccessful"" boolean NOT NULL,
                    CONSTRAINT ""PK_CommandUsages"" PRIMARY KEY (""Id"")
                );"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_CommandUsages_CommandId"" ON ""CommandUsages"" (""CommandId"");"
            );

            // ── Columns added by the deleted intermediate migration ───────────────────────────────────
            // On existing installs these already exist; ADD COLUMN IF NOT EXISTS makes this idempotent.
            migrationBuilder.Sql(
                @"ALTER TABLE ""Timers"" ADD COLUMN IF NOT EXISTS ""ConfigSchemaVersion"" integer NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""Pipelines"" ADD COLUMN IF NOT EXISTS ""TriggerKind"" character varying(30) NOT NULL DEFAULT 'manual';"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""Pipelines"" ADD COLUMN IF NOT EXISTS ""MaxStepCount"" integer NOT NULL DEFAULT 0;"
            );
            // The Initial migration created TriggerCount as integer; the deleted migration widened it to bigint.
            // Widening int→bigint is always safe and needs no USING clause.
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'Pipelines' AND column_name = 'TriggerCount') = 'integer' THEN
                        ALTER TABLE ""Pipelines"" ALTER COLUMN ""TriggerCount"" TYPE bigint;
                    END IF;
                END $$;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""EventResponses"" ADD COLUMN IF NOT EXISTS ""ConfigSchemaVersion"" integer NOT NULL DEFAULT 0;"
            );
            // PipelineId FK columns — the deleted migration added these nullable uuid columns to both tables.
            migrationBuilder.Sql(
                @"ALTER TABLE ""EventResponses"" ADD COLUMN IF NOT EXISTS ""PipelineId"" uuid;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""Timers"" ADD COLUMN IF NOT EXISTS ""PipelineId"" uuid;"
            );
            // The deleted migration renamed EventResponses.Metadata → MetadataJson; undo only when the
            // old name still exists and the new name does not, so this is safe on both paths.
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'EventResponses' AND column_name = 'Metadata')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                                       WHERE table_schema = 'public' AND table_name = 'EventResponses' AND column_name = 'MetadataJson') THEN
                        ALTER TABLE ""EventResponses"" RENAME COLUMN ""Metadata"" TO ""MetadataJson"";
                    END IF;
                END $$;"
            );

            // IF EXISTS guards for FKs / indexes that were in the same deleted dev-only migration.
            migrationBuilder.Sql(
                @"ALTER TABLE ""EventResponses"" DROP CONSTRAINT IF EXISTS ""FK_EventResponses_Pipelines_PipelineId"";"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""Timers"" DROP CONSTRAINT IF EXISTS ""FK_Timers_Pipelines_PipelineId"";"
            );

            // All DropIndex calls use IF EXISTS SQL because these indexes may not exist on a fresh install
            // (they'd only be created by the now-deleted intermediate migration) — but will be created above.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Timers_BroadcasterId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PipelineSteps_PipelineId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Pipelines_BroadcasterId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PipelineExecutions_PipelineId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_NamedCounters_BroadcasterId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_EventResponses_BroadcasterId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommandUsages_CommandId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_CommandCooldownStates_CommandId"";");
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS ""IX_ChannelBuiltinCommands_BroadcasterId"";"
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

            // text[] → jsonb requires an explicit USING clause; EF Core does not generate one.
            // Guard: skip if the column is already jsonb (fixed Initial migration creates it as jsonb).
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='Timers' AND column_name='Messages') <> 'jsonb' THEN
                        ALTER TABLE ""Timers"" ALTER COLUMN ""Messages"" TYPE jsonb USING to_jsonb(""Messages"");
                    END IF;
                END $$;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""Timers"" ALTER COLUMN ""Messages"" SET DEFAULT '[]'::jsonb;"
            );
            migrationBuilder.Sql(@"ALTER TABLE ""Timers"" ALTER COLUMN ""Messages"" SET NOT NULL;");

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

            // hstore → jsonb requires an explicit USING clause; EF Core does not generate one.
            // Guard: skip if already jsonb (fixed Initial migration creates MetadataJson as jsonb directly).
            // data_type for hstore is 'USER-DEFINED'; jsonb is 'jsonb'.
            migrationBuilder.Sql(
                @"
                DO $$
                BEGIN
                    IF (SELECT data_type FROM information_schema.columns
                        WHERE table_schema='public' AND table_name='EventResponses' AND column_name='MetadataJson') = 'USER-DEFINED' THEN
                        ALTER TABLE ""EventResponses"" ALTER COLUMN ""MetadataJson"" TYPE jsonb USING hstore_to_jsonb(""MetadataJson"");
                    END IF;
                END $$;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""EventResponses"" ALTER COLUMN ""MetadataJson"" SET DEFAULT '{}'::jsonb;"
            );
            migrationBuilder.Sql(
                @"ALTER TABLE ""EventResponses"" ALTER COLUMN ""MetadataJson"" SET NOT NULL;"
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
