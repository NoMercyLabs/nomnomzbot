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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S019a — proves the <see cref="PlatformConnection"/> EF mapping and the generated
/// <c>AddPlatformConnection</c> migration actually work end-to-end against a real SQLite database (not just
/// that the class compiles): a Channel gets two PlatformConnection rows (Twitch + Kick), both round-trip
/// through the FK with correct field values once re-queried via <c>Channel.PlatformConnections</c>.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class PlatformConnectionPersistenceTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.Parse("0198e000-0000-7000-8000-0000000000a1");
    private static readonly Guid ChannelId = Guid.Parse("0198e000-0000-7000-8000-0000000000a2");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_platformconnection_{Guid.NewGuid():N}.db"
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
    public async Task Two_platform_connections_round_trip_through_the_channel_fk_after_migration()
    {
        // Arrange: migrate a fresh SQLite file all the way up (proves the AddPlatformConnection
        // migration itself applies cleanly), then seed a Channel with two PlatformConnection rows.
        await using (AppDbContext migrateContext = NewContext())
        {
            IMigrator migrator = migrateContext.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync();
        }

        await using (AppDbContext seedContext = NewContext())
        {
            seedContext.Users.Add(
                new()
                {
                    Id = OwnerId,
                    Username = "stoney",
                    UsernameNormalized = "stoney",
                    DisplayName = "Stoney",
                }
            );
            seedContext.Channels.Add(
                new()
                {
                    Id = ChannelId,
                    OwnerUserId = OwnerId,
                    TwitchChannelId = "tw-owner",
                    ExternalChannelId = "tw-owner",
                    Name = "stoney",
                    NameNormalized = "stoney",
                }
            );
            seedContext.PlatformConnections.AddRange(
                new PlatformConnection
                {
                    ChannelId = ChannelId,
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = "tw-owner",
                    DisplayName = "Stoney (Twitch)",
                    IsPrimary = true,
                    IsLive = true,
                },
                new PlatformConnection
                {
                    ChannelId = ChannelId,
                    Provider = AuthEnums.Platform.Kick,
                    ExternalChannelId = "kick-owner",
                    DisplayName = "Stoney (Kick)",
                    IsPrimary = false,
                    IsLive = false,
                }
            );
            await seedContext.SaveChangesAsync();
        }

        // Act: re-query the Channel with its PlatformConnections included through a brand-new context
        // (a real read against the persisted database, not the same tracked graph that wrote it).
        await using AppDbContext assertContext = NewContext();
        Channel? channel = await assertContext
            .Channels.Include(c => c.PlatformConnections)
            .SingleOrDefaultAsync(c => c.Id == ChannelId);

        // Assert: both rows survived the round trip with the correct FK and field values.
        channel.Should().NotBeNull();
        channel!.PlatformConnections.Should().HaveCount(2);

        PlatformConnection twitch = channel.PlatformConnections.Single(p =>
            p.Provider == AuthEnums.Platform.Twitch
        );
        twitch.ChannelId.Should().Be(ChannelId);
        twitch.ExternalChannelId.Should().Be("tw-owner");
        twitch.DisplayName.Should().Be("Stoney (Twitch)");
        twitch.IsPrimary.Should().BeTrue();
        twitch.IsLive.Should().BeTrue();

        PlatformConnection kick = channel.PlatformConnections.Single(p =>
            p.Provider == AuthEnums.Platform.Kick
        );
        kick.ChannelId.Should().Be(ChannelId);
        kick.ExternalChannelId.Should().Be("kick-owner");
        kick.DisplayName.Should().Be("Stoney (Kick)");
        kick.IsPrimary.Should().BeFalse();
        kick.IsLive.Should().BeFalse();

        // The (Provider, ExternalChannelId) unique index must be enforced going forward.
        assertContext.PlatformConnections.Add(
            new PlatformConnection
            {
                ChannelId = ChannelId,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-owner",
                DisplayName = "Duplicate",
            }
        );
        Func<Task> insertDuplicate = () => assertContext.SaveChangesAsync();
        await insertDuplicate
            .Should()
            .ThrowAsync<DbUpdateException>(
                "the (Provider, ExternalChannelId) unique index must be enforced"
            );
    }
}
