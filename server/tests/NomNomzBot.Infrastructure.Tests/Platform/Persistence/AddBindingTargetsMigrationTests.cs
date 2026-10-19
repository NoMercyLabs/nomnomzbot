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
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// The <c>AddBindingTargets</c> migration gives seven pipeline-bound surfaces the target columns and copies each
/// automation token's pipeline allow-list into the target allow-list. Proven on a real SQLite file: rows written
/// BEFORE the migration are seeded with raw SQL, then read through the current entity model.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class AddBindingTargetsMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261019042628_AddSpamDefenseTrustThresholds";

    private static readonly string[] Surfaces =
    [
        "Rewards",
        "Timers",
        "EventResponses",
        "ChatTriggers",
        "ScheduledPipelineTasks",
        "Giveaways",
        "CatalogItems",
    ];

    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid OldReward = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid OldPipeline = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");
    private static readonly Guid OtherPipeline = Guid.Parse("0192a000-0000-7000-8000-0000000000e4");
    private static readonly Guid TokenId = Guid.Parse("0192a000-0000-7000-8000-0000000000e5");
    private static readonly Guid OpenTokenId = Guid.Parse("0192a000-0000-7000-8000-0000000000e6");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-0000000000e7");
    private static readonly DateTime Stamp = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_binding_targets_{Guid.NewGuid():N}.db"
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

    /// <summary>Migrates to the previous schema, seeds an old reward and two old tokens, then migrates up.</summary>
    private async Task MigrateWithOldRowsAsync()
    {
        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            await seedContext.Database.OpenConnectionAsync();
            await seedContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");

            string pinned = $"[\"{OldPipeline}\",\"{OtherPipeline}\"]";
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Rewards"
                    ("Id", "BroadcasterId", "Title", "PipelineId", "IsManageable", "IsPaused", "IsPlatform",
                     "IsUserInputRequired", "CreatedAt", "UpdatedAt")
                VALUES ({OldReward}, {Broadcaster}, 'old reward', {OldPipeline}, 0, 0, 0, 0, {Stamp}, {Stamp})
                """
            );
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "AutomationApiTokens"
                    ("Id", "BroadcasterId", "Name", "TokenHash", "TokenPrefix", "ScopesJson",
                     "AllowedPipelineIdsJson", "CreatedByUserId", "CreatedAt", "UpdatedAt")
                VALUES
                    ({TokenId}, {Broadcaster}, 'pinned', 'h1', 'p1', '[]', {pinned}, {Broadcaster}, {Stamp}, {Stamp}),
                    ({OpenTokenId}, {Broadcaster}, 'open', 'h2', 'p2', '[]', NULL, {Broadcaster}, {Stamp}, {Stamp})
                """
            );
        }

        await using AppDbContext migrateContext = NewContext();
        IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync();
    }

    [Fact]
    public async Task Every_surface_gets_the_target_columns_and_kind_defaults_to_pipeline()
    {
        await MigrateWithOldRowsAsync();

        await using SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        foreach (string surface in Surfaces)
        {
            Dictionary<string, string?> defaults = [];
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{surface}\")";
            await using (SqliteDataReader reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    defaults[reader.GetString(1)] = reader.IsDBNull(4) ? null : reader.GetString(4);
            }

            defaults
                .Keys.Should()
                .Contain(
                    ["TargetKind", "TargetCommandId", "TargetActionType", "TargetActionParamsJson"],
                    $"{surface} carries the binding target columns"
                );
            defaults["TargetKind"]
                .Should()
                .Be("1", $"{surface}: a row without a kind is a pipeline (Pipeline = 1)");
        }
    }

    [Fact]
    public async Task A_row_saved_before_the_migration_reads_as_a_pipeline_target_with_its_pipeline_id()
    {
        await MigrateWithOldRowsAsync();

        await using AppDbContext context = NewContext();
        Reward reward = await context
            .Rewards.IgnoreQueryFilters()
            .SingleAsync(r => r.Id == OldReward);

        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        reward.TargetCommandId.Should().BeNull();
        reward
            .GetBindingTarget()
            .Should()
            .Be(new BindingTarget(BindingTargetKind.Pipeline, OldPipeline.ToString()));
    }

    [Fact]
    public async Task A_reward_saved_with_a_command_target_round_trips_it()
    {
        await MigrateWithOldRowsAsync();
        Guid rewardId = Guid.CreateVersion7();

        await using (AppDbContext writer = NewContext())
        {
            await writer.Database.OpenConnectionAsync();
            await writer.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            Reward reward = new()
            {
                Id = rewardId,
                BroadcasterId = Broadcaster,
                Title = "command reward",
            };
            reward.SetBindingTarget(new(BindingTargetKind.Command, CommandId.ToString()));
            writer.Rewards.Add(reward);
            await writer.SaveChangesAsync();
        }

        await using AppDbContext reader = NewContext();
        Reward loaded = await reader
            .Rewards.IgnoreQueryFilters()
            .SingleAsync(r => r.Id == rewardId);

        loaded.TargetKind.Should().Be(BindingTargetKind.Command);
        loaded.TargetCommandId.Should().Be(CommandId);
        loaded.PipelineId.Should().BeNull();
        loaded
            .GetBindingTarget()
            .Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandId.ToString()));
    }

    [Fact]
    public async Task The_token_pipeline_list_is_copied_as_pipeline_targets_and_the_old_list_still_authorizes()
    {
        await MigrateWithOldRowsAsync();

        await using AppDbContext context = NewContext();
        AutomationApiToken pinned = await context
            .AutomationApiTokens.IgnoreQueryFilters()
            .SingleAsync(t => t.Id == TokenId);
        AutomationApiToken open = await context
            .AutomationApiTokens.IgnoreQueryFilters()
            .SingleAsync(t => t.Id == OpenTokenId);

        BindingTargetJson
            .DeserializeList(pinned.AllowedTargetsJson)
            .Should()
            .Equal(
                new BindingTarget(BindingTargetKind.Pipeline, OldPipeline.ToString()),
                new BindingTarget(BindingTargetKind.Pipeline, OtherPipeline.ToString())
            );
        pinned
            .AllowedPipelineIdsJson.Should()
            .Contain(OldPipeline.ToString(), "the old list stays readable in place");
        pinned.GetAllowedTargets().Should().HaveCount(2);
        open.AllowedTargetsJson.Should().BeNull("an unrestricted token stays unrestricted");
        open.GetAllowedTargets().Should().BeNull();
    }
}
