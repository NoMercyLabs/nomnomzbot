// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S-COMMAND-CODE-SCRIPT (2026-10-22): the <c>AddCommandCodeScriptId</c> migration adds
/// <c>Commands.CodeScriptId</c> and moves every code command whose pipeline is a single <c>run_code</c> step
/// onto it, clearing the wrapper reference. This seeds the shapes found on a live box on a real SQLite file at
/// the schema before the migration, runs the migration, and reads the stored columns.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class AddCommandCodeScriptIdMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261022085139_RepairCurrencyLifetimeTotals";
    private const string TargetMigrationId = "20261022095458_AddCommandCodeScriptId";

    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherChannel = Guid.Parse("0192d000-0000-7000-8000-0000000000a2");
    private static readonly Guid Script = Guid.Parse("0192d000-0000-7000-8000-0000000000b1");
    private static readonly Guid OtherChannelScript = Guid.Parse(
        "0192d000-0000-7000-8000-0000000000b2"
    );
    private static readonly DateTime When = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_code_script_command_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        foreach (
            string path in new[]
            {
                _dbPath,
                $"{_dbPath}-wal",
                $"{_dbPath}-shm",
                $"{_dbPath}-journal",
            }
        )
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private AppDbContext NewContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                ConnectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;
        return new(options);
    }

    [Fact]
    public async Task Migration_moves_a_single_run_code_wrapper_onto_the_command_and_leaves_every_other_command()
    {
        await using (AppDbContext seed = NewContext())
        {
            IMigrator migrator = seed.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigrationId);

            // The seed rows reference channels this raw-SQL fixture does not create; FK checks are per connection.
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

            await AddScriptAsync(seed, Script, Channel, "lucky");
            await AddScriptAsync(seed, OtherChannelScript, OtherChannel, "foreign");

            Guid wrapper = await AddPipelineAsync(seed, "wrapper", Channel);
            await AddStepAsync(seed, wrapper, 0, "run_code", RunCodeJson(Script));

            Guid multi = await AddPipelineAsync(seed, "multi", Channel);
            await AddStepAsync(seed, multi, 0, "run_code", RunCodeJson(Script));
            await AddStepAsync(seed, multi, 1, "send_message", "{\"type\":\"send_message\"}");

            Guid foreign = await AddPipelineAsync(seed, "foreign-wrapper", Channel);
            await AddStepAsync(seed, foreign, 0, "run_code", RunCodeJson(OtherChannelScript));

            await AddCommandAsync(seed, "luckyfeather", "code", wrapper);
            await AddCommandAsync(seed, "bigflow", "code", multi);
            await AddCommandAsync(seed, "borrowed", "code", foreign);
            await AddCommandAsync(seed, "hello", "template", null);
            await AddCommandAsync(seed, "flow", "pipeline", wrapper);
        }

        await using (AppDbContext migrate = NewContext())
        {
            IMigrator migrator = migrate.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(TargetMigrationId);
        }

        Dictionary<string, (string? CodeScriptId, string? PipelineId)> stored =
            await ReadCommandsAsync();

        stored["luckyfeather"]
            .CodeScriptId.Should()
            .BeEquivalentTo(Script.ToString(), "the single run_code step moved onto the command");
        stored["luckyfeather"].PipelineId.Should().BeNull("the wrapper reference is cleared");

        stored["bigflow"]
            .CodeScriptId.Should()
            .BeNull("a pipeline with other steps stays a pipeline");
        stored["bigflow"].PipelineId.Should().NotBeNull();

        stored["borrowed"]
            .CodeScriptId.Should()
            .BeNull("a script of another channel is never adopted");
        stored["borrowed"].PipelineId.Should().NotBeNull();

        stored["hello"].CodeScriptId.Should().BeNull();
        stored["flow"].CodeScriptId.Should().BeNull("only code commands are moved");
        stored["flow"].PipelineId.Should().NotBeNull();
    }

    private static string RunCodeJson(Guid scriptId) =>
        $"{{\"type\":\"run_code\",\"code_script_id\":\"{scriptId}\"}}";

    private static Task AddScriptAsync(AppDbContext db, Guid id, Guid channel, string name) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "CodeScripts" ("Id", "BroadcasterId", "Name", "Language", "IsEnabled", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {channel}, {name}, 'typescript', 1, {When}, {When})
            """
        );

    private static async Task<Guid> AddPipelineAsync(AppDbContext db, string name, Guid channel)
    {
        Guid id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Pipelines" ("Id", "BroadcasterId", "Name", "TriggerKind", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {channel}, {name}, 'manual', {When}, {When})
            """
        );
        return id;
    }

    private static Task AddStepAsync(
        AppDbContext db,
        Guid pipeline,
        int order,
        string actionType,
        string configJson
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "PipelineSteps"
                ("Id", "PipelineId", "BroadcasterId", "Order", "ActionType", "ConfigJson",
                 "ContinueOnError", "StopOnMatch", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.CreateVersion7()}, {pipeline}, {Channel}, {order}, {actionType}, {configJson},
                 0, 0, {When}, {When})
            """
        );

    private static Task AddCommandAsync(
        AppDbContext db,
        string name,
        string tier,
        Guid? pipeline
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Commands"
                ("Id", "BroadcasterId", "Name", "NameNormalized", "Tier", "PipelineId", "MatchMode", "PrefixMode", "Aliases",
                 "ConfigSchemaVersion", "CooldownSeconds", "UserCooldownSeconds", "IsPlatform", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.CreateVersion7()}, {Channel}, {name}, {name}, {tier}, {pipeline}, 'exact', 'default', '[]',
                 1, 0, 0, 0, {When}, {When})
            """
        );

    private async Task<
        Dictionary<string, (string? CodeScriptId, string? PipelineId)>
    > ReadCommandsAsync()
    {
        await using SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"Name\", \"CodeScriptId\", \"PipelineId\" FROM \"Commands\"";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        Dictionary<string, (string? CodeScriptId, string? PipelineId)> rows = [];
        while (await reader.ReadAsync())
            rows[reader.GetString(0)] = (
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)
            );
        return rows;
    }
}
