// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Data;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The one-time rewrite of saved template text from the retired short keys to their canonical keys
/// (<c>{space}</c> becomes <c>{chars.space}</c>). Proves the exact rewritten text for every template column,
/// that escaped braces and arguments survive, and that the real SQLite migration chain runs the rewrite.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class RewriteRetiredTemplateKeysMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261026211033_AddOfflineAutoOff";

    // Old keys in three spellings, with arguments, inside a nested helper, next to an escaped brace, a longer
    // key that only starts like a retired one, and a canonical key that must stay as it is.
    private const string PlainOld =
        "{space}hi{Newline}there {pipe:x} {if:a|{ZWS}} \\{space} {spaceship} {chars.space} {nothing}";
    private const string PlainNew =
        "{chars.space}hi{chars.newline}there {chars.pipe:x} {if:a|{chars.zws}} \\{space} {spaceship} {chars.space} {chars.nothing}";

    // The same text as it is stored inside a JSON document: the backslash is doubled.
    private const string JsonOld =
        "{\"text\":\"{space}hi {pipe:x}\",\"escaped\":\"\\\\{space}\",\"list\":[\"{newline}\"]}";
    private const string JsonNew =
        "{\"text\":\"{chars.space}hi {chars.pipe:x}\",\"escaped\":\"\\\\{space}\",\"list\":[\"{chars.newline}\"]}";

    // The include rename: an include in two spellings with an argument, next to the running-command key that
    // stays, an escaped brace and a longer key that only starts like the old one.
    private const string IncludeSchemaMigrationId = "20261027202710_AddViewerActivitySteps";
    private const string IncludeOld =
        "{include.command:hug} {Include.command:wave|x} {command.name} \\{include.command:hug} {include.commandx}";
    private const string IncludeNew =
        "{include.cmd:hug} {include.cmd:wave|x} {command.name} \\{include.command:hug} {include.commandx}";

    private const string Untouched = "{chars.space} {user} {spaceship} \\{space} plain";

    private static readonly Guid OwnerUserId = Guid.Parse("0192b200-0000-7000-8000-0000000000b1");
    private static readonly Guid ChannelId = Guid.Parse("0192b200-0000-7000-8000-0000000000b2");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_template_keys_{Guid.NewGuid():N}.db"
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
    public void The_map_holds_the_five_retired_character_keys()
    {
        RetiredTemplateKeyMap
            .Pairs.Select(p => $"{p.Key}->{p.Value}")
            .Should()
            .Equal(
                "nothing->chars.nothing",
                "newline->chars.newline",
                "space->chars.space",
                "pipe->chars.pipe",
                "zws->chars.zws"
            );
    }

    [Fact]
    public void Every_template_column_gets_exactly_the_rewritten_text()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        // A table can hold several template columns (Commands has two): create it once, add the rest.
        // Each column owns its own row ids (index * 10 + 1..4), so the columns never share a row.
        HashSet<string> createdTables = [];
        for (int index = 0; index < TemplateTextColumn.All.Count; index++)
        {
            TemplateTextColumn column = TemplateTextColumn.All[index];
            int baseId = index * 10;
            if (createdTables.Add(column.Table))
                Execute(
                    connection,
                    $"CREATE TABLE \"{column.Table}\" (\"Id\" INTEGER, \"{column.Column}\" TEXT)"
                );
            else
                Execute(
                    connection,
                    $"ALTER TABLE \"{column.Table}\" ADD COLUMN \"{column.Column}\" TEXT"
                );
            Insert(connection, column, baseId + 1, PlainOld);
            Insert(connection, column, baseId + 2, JsonOld);
            Insert(connection, column, baseId + 3, Untouched);
            Insert(connection, column, baseId + 4, null);
        }

        foreach (string statement in RetiredTemplateKeySql.Statements(TemplateSqlDialect.Sqlite))
            Execute(connection, statement);

        for (int index = 0; index < TemplateTextColumn.All.Count; index++)
        {
            TemplateTextColumn column = TemplateTextColumn.All[index];
            int baseId = index * 10;
            string label = $"{column.Table}.{column.Column}";
            Read(connection, column, baseId + 1).Should().Be(PlainNew, label);
            Read(connection, column, baseId + 2).Should().Be(JsonNew, label);
            Read(connection, column, baseId + 3).Should().Be(Untouched, label);
            Read(connection, column, baseId + 4).Should().BeNull(label);
        }
    }

    [Fact]
    public void Running_the_rewrite_a_second_time_changes_nothing()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        TemplateTextColumn column = TemplateTextColumn.All[0];
        Execute(
            connection,
            $"CREATE TABLE \"{column.Table}\" (\"Id\" INTEGER, \"{column.Column}\" TEXT)"
        );
        Insert(connection, column, 1, PlainOld);
        IReadOnlyList<string> statements = RetiredTemplateKeySql.Statements(
            TemplateSqlDialect.Sqlite
        );
        foreach (
            string statement in statements.Where(s =>
                s.StartsWith($"UPDATE \"{column.Table}\" SET \"{column.Column}\"")
            )
        )
            Execute(connection, statement);

        foreach (
            string statement in statements.Where(s =>
                s.StartsWith($"UPDATE \"{column.Table}\" SET \"{column.Column}\"")
            )
        )
            Execute(connection, statement);

        Read(connection, column, 1).Should().Be(PlainNew);
    }

    [Fact]
    public void Postgres_statements_cast_json_and_arrays_and_guard_the_varchar_limits()
    {
        IReadOnlyList<string> statements = RetiredTemplateKeySql.Statements(
            TemplateSqlDialect.Postgres
        );

        statements.Should().HaveCount(TemplateTextColumn.All.Count);
        foreach (TemplateTextColumn column in TemplateTextColumn.All)
        {
            string statement = statements.Single(s =>
                s.StartsWith($"UPDATE \"{column.Table}\" SET \"{column.Column}\" =")
            );
            statement
                .Contains("::jsonb")
                .Should()
                .Be(column.Shape == TemplateTextColumnShape.Jsonb, column.Column);
            statement
                .Contains("unnest(")
                .Should()
                .Be(column.Shape == TemplateTextColumnShape.TextArray, column.Column);
            statement
                .Contains(" AND LENGTH(")
                .Should()
                .Be(column.MaxLength is not null, column.Column);
            if (column.MaxLength is int max)
                statement.Should().EndWith($"<= {max};", column.Column);
        }
    }

    [Fact]
    public void Every_column_in_the_list_exists_in_the_model_under_that_table_and_column_name()
    {
        using AppDbContext context = NewContext();

        foreach (TemplateTextColumn column in TemplateTextColumn.All)
        {
            IEntityType? entity = context
                .Model.GetEntityTypes()
                .SingleOrDefault(e => e.GetTableName() == column.Table);
            entity.Should().NotBeNull($"table {column.Table}");
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(
                column.Table,
                entity!.GetSchema()
            );
            entity
                .GetProperties()
                .Select(p => p.GetColumnName(table))
                .Should()
                .Contain(column.Column, $"{column.Table}.{column.Column}");
        }
    }

    [Fact]
    public void Every_templated_user_content_property_is_in_the_list()
    {
        using AppDbContext context = NewContext();
        List<string> listed = [.. TemplateTextColumn.All.Select(c => $"{c.Table}.{c.Column}")];

        foreach (IEntityType entity in context.Model.GetEntityTypes())
        {
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(
                entity.GetTableName()!,
                entity.GetSchema()
            );
            foreach (IProperty property in entity.GetProperties())
            {
                PropertyInfo? info = property.PropertyInfo;
                if (
                    info is null
                    || !info.IsDefined(typeof(TemplatedUserContentAttribute), inherit: true)
                )
                    continue;

                listed
                    .Should()
                    .Contain(
                        $"{table.Name}.{property.GetColumnName(table)}",
                        "a templated column must be rewritten"
                    );
            }
        }
    }

    [Fact]
    public async Task The_real_migration_chain_rewrites_a_saved_shoutout_and_keeps_the_rest_of_the_row()
    {
        await SeedShoutoutAtAsync(PreviousMigrationId, PlainOld);

        await using (AppDbContext migrateContext = NewContext())
            await migrateContext.Database.MigrateAsync();

        (string shoutout, string name) = await ReadChannelAsync();
        shoutout.Should().Be(PlainNew);
        name.Should().Be("streamer", "the rewrite touches template text only");
    }

    [Fact]
    public void The_include_map_holds_the_one_renamed_include_key()
    {
        RetiredIncludeKeyMap
            .Pairs.Select(p => $"{p.Key}->{p.Value}")
            .Should()
            .Equal("include.command->include.cmd");
    }

    [Fact]
    public void Every_template_column_gets_the_include_key_rewritten_and_escaped_text_is_kept()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        HashSet<string> createdTables = [];
        // A table can hold several template columns: each column owns its own row id.
        for (int index = 0; index < TemplateTextColumn.All.Count; index++)
        {
            TemplateTextColumn column = TemplateTextColumn.All[index];
            if (createdTables.Add(column.Table))
                Execute(
                    connection,
                    $"CREATE TABLE \"{column.Table}\" (\"Id\" INTEGER, \"{column.Column}\" TEXT)"
                );
            else
                Execute(
                    connection,
                    $"ALTER TABLE \"{column.Table}\" ADD COLUMN \"{column.Column}\" TEXT"
                );
            Insert(connection, column, index + 1, IncludeOld);
        }

        foreach (
            string statement in RetiredTemplateKeySql.Statements(
                TemplateSqlDialect.Sqlite,
                RetiredIncludeKeyMap.Pairs
            )
        )
            Execute(connection, statement);

        for (int index = 0; index < TemplateTextColumn.All.Count; index++)
        {
            TemplateTextColumn column = TemplateTextColumn.All[index];
            Read(connection, column, index + 1)
                .Should()
                .Be(IncludeNew, $"{column.Table}.{column.Column}");
        }
    }

    [Fact]
    public async Task The_real_migration_chain_rewrites_a_saved_include_and_keeps_the_running_command_key()
    {
        await SeedShoutoutAtAsync(IncludeSchemaMigrationId, IncludeOld);

        await using (AppDbContext migrateContext = NewContext())
            await migrateContext.Database.MigrateAsync();

        (string shoutout, string name) = await ReadChannelAsync();
        shoutout.Should().Be(IncludeNew);
        name.Should().Be("streamer");
    }

    private async Task SeedShoutoutAtAsync(string migrationId, string shoutoutTemplate)
    {
        await using AppDbContext seedContext = NewContext();
        IMigrator migrator = seedContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(migrationId);

        DateTime now = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        await seedContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Users"
                ("Id", "DisplayName", "Username", "UsernameNormalized", "Platform", "Type",
                 "IsAnonymized", "IsBot", "IsLurking", "IsPlatformPrincipal", "PronounManualOverride",
                 "CreatedAt", "UpdatedAt")
            VALUES
                ({OwnerUserId}, 'Streamer', 'streamer', 'streamer', 'twitch', 'user',
                 0, 0, 0, 0, 0, {now}, {now})
            """
        );
        await seedContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Channels"
                ("Id", "OwnerUserId", "Name", "NameNormalized", "OverlayToken", "AnnounceOnConnect",
                 "BillingTierKey", "ContentLabels", "DeploymentMode", "ExternalChannelId",
                 "IsBrandedContent", "IsLive", "IsOnboarded", "Provider", "Status", "StreamDelay",
                 "Tags", "ShoutoutTemplate", "CreatedAt", "UpdatedAt")
            VALUES
                ({ChannelId}, {OwnerUserId}, 'streamer', 'streamer', 'tok', 1,
                 'free', '[]', 'self_host_full', 'ext-1',
                 0, 0, 1, 'twitch', 'active', 0,
                 '[]', {shoutoutTemplate}, {now}, {now})
            """
        );
    }

    private async Task<(string Shoutout, string Name)> ReadChannelAsync()
    {
        await using SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"ShoutoutTemplate\", \"Name\" FROM \"Channels\"";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.GetString(1));
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(
        SqliteConnection connection,
        TemplateTextColumn column,
        int id,
        string? text
    )
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO \"{column.Table}\" (\"Id\", \"{column.Column}\") VALUES ($id, $text)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static string? Read(SqliteConnection connection, TemplateTextColumn column, int id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"{column.Column}\" FROM \"{column.Table}\" WHERE \"Id\" = $id";
        command.Parameters.AddWithValue("$id", id);
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : (string)value;
    }
}
