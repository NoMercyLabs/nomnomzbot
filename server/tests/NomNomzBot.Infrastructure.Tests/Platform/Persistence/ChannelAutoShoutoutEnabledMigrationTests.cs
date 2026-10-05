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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// A channel's auto-shoutout setting is opt-in: a new channel reads off, the owner can turn it on and it
/// survives a reload, and a channel that existed before the migration reads off after the upgrade. Runs the
/// real SQLite migration chain, so a missing or wrong migration fails here.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class ChannelAutoShoutoutEnabledMigrationTests : IDisposable
{
    private const string PreviousMigrationId = "20261003051856_AddChannelAudioMix";

    private static readonly Guid OwnerUserId = Guid.Parse("0192b200-0000-7000-8000-0000000000a1");
    private static readonly Guid ExistingChannelId = Guid.Parse(
        "0192b200-0000-7000-8000-0000000000a2"
    );
    private static readonly Guid NewChannelId = Guid.Parse("0192b200-0000-7000-8000-0000000000a3");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_auto_shoutout_{Guid.NewGuid():N}.db"
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
    public async Task New_channel_reads_off_and_a_turned_on_setting_survives_a_reload()
    {
        await using (AppDbContext migrateContext = NewContext())
            await migrateContext.Database.MigrateAsync();

        await using (AppDbContext writeContext = NewContext())
        {
            writeContext.Users.Add(
                new()
                {
                    Id = OwnerUserId,
                    Username = "streamer",
                    UsernameNormalized = "streamer",
                    DisplayName = "Streamer",
                }
            );
            writeContext.Channels.Add(
                new()
                {
                    Id = NewChannelId,
                    OwnerUserId = OwnerUserId,
                    ExternalChannelId = "ext-new",
                    Name = "newstreamer",
                    NameNormalized = "newstreamer",
                }
            );
            await writeContext.SaveChangesAsync();
        }

        await using (AppDbContext readContext = NewContext())
        {
            Channel channel = await readContext.Channels.SingleAsync(c => c.Id == NewChannelId);
            channel
                .AutoShoutoutEnabled.Should()
                .BeFalse("auto-shoutout is off unless the owner turns it on");
            channel.AutoShoutoutEnabled = true;
            await readContext.SaveChangesAsync();
        }

        await using AppDbContext assertContext = NewContext();
        Channel reloaded = await assertContext.Channels.SingleAsync(c => c.Id == NewChannelId);
        reloaded.AutoShoutoutEnabled.Should().BeTrue("the owner's choice must persist");
    }

    [Fact]
    public async Task Channel_that_existed_before_the_migration_reads_off_after_the_upgrade()
    {
        await using (AppDbContext seedContext = NewContext())
        {
            IMigrator seedMigrator = seedContext
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();
            await seedMigrator.MigrateAsync(PreviousMigrationId);

            DateTime now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
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
                     "Tags", "CreatedAt", "UpdatedAt")
                VALUES
                    ({ExistingChannelId}, {OwnerUserId}, 'oldstreamer', 'oldstreamer', 'legacy-tok', 1,
                     'free', '[]', 'self_host_full', 'ext-old',
                     0, 0, 1, 'twitch', 'active', 0,
                     '[]', {now}, {now})
                """
            );
        }

        await using (AppDbContext migrateContext = NewContext())
            await migrateContext.Database.MigrateAsync();

        await using AppDbContext assertContext = NewContext();
        Channel channel = await assertContext.Channels.SingleAsync(c => c.Id == ExistingChannelId);
        channel
            .AutoShoutoutEnabled.Should()
            .BeFalse("an upgrade must never switch a channel's auto-shoutout on");
        channel.AnnounceOnConnect.Should().BeTrue("the upgrade must leave the existing row intact");
    }
}
