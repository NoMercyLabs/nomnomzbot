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

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NomNomzBot.Migrations.Sqlite.Migrations;

namespace NomNomzBot.Infrastructure.Tests.Commands;

public sealed class TtsSwitchMigrationTests
{
    [Fact]
    public void Existing_rows_keep_their_speech_behavior_after_sqlite_migration()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        Execute(
            connection,
            """
            CREATE TABLE "Commands" ("Id" TEXT, "PipelineId" TEXT);
            CREATE TABLE "Timers" ("Id" TEXT, "PipelineId" TEXT);
            CREATE TABLE "Pipelines" ("Id" TEXT, "GraphJsonCache" TEXT);
            CREATE TABLE "PipelineSteps" ("PipelineId" TEXT, "ActionType" TEXT);
            INSERT INTO "Commands" VALUES ('plain', NULL), ('spoken', 'flow');
            INSERT INTO "Timers" VALUES ('plain', NULL), ('spoken', 'flow');
            INSERT INTO "Pipelines" VALUES ('flow', '{"steps":[]}');
            INSERT INTO "PipelineSteps" VALUES ('flow', 'play_tts');
            """
        );

        AddCommandAndTimerSpeakWithTts migration = new();
        foreach (MigrationOperation operation in migration.UpOperations)
        {
            if (operation is AddColumnOperation column)
                Execute(
                    connection,
                    $"ALTER TABLE \"{column.Table}\" ADD COLUMN \"{column.Name}\" INTEGER NOT NULL DEFAULT 0;"
                );
            else if (operation is SqlOperation sql)
                Execute(connection, sql.Sql);
        }

        Scalar(connection, "SELECT \"SpeakWithTts\" FROM \"Commands\" WHERE \"Id\"='plain'")
            .Should()
            .Be(0);
        Scalar(connection, "SELECT \"SpeakWithTts\" FROM \"Commands\" WHERE \"Id\"='spoken'")
            .Should()
            .Be(1);
        Scalar(connection, "SELECT \"SpeakWithTts\" FROM \"Timers\" WHERE \"Id\"='plain'")
            .Should()
            .Be(0);
        Scalar(connection, "SELECT \"SpeakWithTts\" FROM \"Timers\" WHERE \"Id\"='spoken'")
            .Should()
            .Be(1);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
