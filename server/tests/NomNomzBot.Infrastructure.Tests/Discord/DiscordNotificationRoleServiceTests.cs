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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the notify-role service (discord.md §3.3). Proves the role row + uniqueness, and that an
/// opt-in records the row, assigns the Discord role through the gateway, and publishes the opt-in event; opt-out
/// removes the role and publishes the inverse.
/// </summary>
public sealed class DiscordNotificationRoleServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 14, 0, 0, TimeSpan.Zero)
    );

    [Fact]
    public async Task CreateRoleAsync_PersistsRole_ThenDuplicateRoleIsAlreadyExists()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(database, channel);

        CreateDiscordNotificationRoleRequest request = new("role-77", "Notify Squad", true);

        DiscordNotificationRoleDto created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationRoleDto> result = await NewService(db, new RecordingGateway())
                .CreateRoleAsync(channel, connectionId, request);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            created = result.Value;
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationRole stored = await db.DiscordNotificationRoles.SingleAsync();
            stored.Id.Should().Be(created.Id);
            stored.DiscordRoleId.Should().Be("role-77");
            stored.RoleName.Should().Be("Notify Squad");
            stored.SelfAssignEnabled.Should().BeTrue();
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationRoleDto> dup = await NewService(db, new RecordingGateway())
                .CreateRoleAsync(channel, connectionId, request);
            dup.IsFailure.Should().BeTrue();
            dup.ErrorCode.Should().Be("ALREADY_EXISTS");
        }
    }

    [Fact]
    public async Task OptInMemberAsync_RecordsOptIn_AssignsRoleViaGateway_AndPublishesEvent()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(database, channel, guildId: "guild-abc");
        Guid roleId = await SeedRoleAsync(database, channel, connectionId, "discord-role-9");
        RecordingGateway gateway = new();
        RecordingEventBus bus = new();

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result result = await NewService(db, gateway, bus)
                .OptInMemberAsync(channel, roleId, "member-1", "button");
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        // The opt-in row is recorded with the source, active (no opt-out).
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordMemberOptIn stored = await db.DiscordMemberOptIns.SingleAsync();
            stored.DiscordMemberId.Should().Be("member-1");
            stored.OptInSource.Should().Be("button");
            stored.OptedOutAt.Should().BeNull();
        }

        // The Discord role was assigned through the gateway with the right guild/member/role.
        gateway.RoleAdds.Should().ContainSingle();
        gateway.RoleAdds[0].Should().Be(("guild-abc", "member-1", "discord-role-9"));

        // The opt-in-changed event fired (opted in = true).
        DiscordMemberOptInChangedEvent ev = bus
            .Published.OfType<DiscordMemberOptInChangedEvent>()
            .Single();
        ev.OptedIn.Should().BeTrue();
        ev.DiscordMemberId.Should().Be("member-1");
    }

    [Fact]
    public async Task OptOutMemberAsync_RemovesRoleViaGateway_AndPublishesOptedOut()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(database, channel, guildId: "guild-abc");
        Guid roleId = await SeedRoleAsync(database, channel, connectionId, "discord-role-9");
        RecordingGateway gateway = new();
        RecordingEventBus bus = new();

        // Opt in first.
        await using (DiscordTestDbContext db = database.NewContext())
            await NewService(db, gateway, new RecordingEventBus())
                .OptInMemberAsync(channel, roleId, "member-1", "button");

        // Then opt out.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result result = await NewService(db, gateway, bus)
                .OptOutMemberAsync(channel, roleId, "member-1", "button");
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordMemberOptIn stored = await db.DiscordMemberOptIns.SingleAsync();
            stored.OptedOutAt.Should().NotBeNull();
        }

        gateway.RoleRemoves.Should().ContainSingle();
        gateway.RoleRemoves[0].Should().Be(("guild-abc", "member-1", "discord-role-9"));
        bus.Published.OfType<DiscordMemberOptInChangedEvent>().Single().OptedIn.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteRoleAsync_NullsPingRoleOnReferencingConfigs()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(database, channel);
        Guid roleId = await SeedRoleAsync(database, channel, connectionId, "discord-role-1");

        // A config that pings this role.
        Guid configId = Guid.CreateVersion7();
        await using (DiscordTestDbContext db = database.NewContext())
        {
            db.DiscordNotificationConfigs.Add(
                new DiscordNotificationConfig
                {
                    Id = configId,
                    BroadcasterId = channel,
                    GuildConnectionId = connectionId,
                    TriggerType = "go_live",
                    Enabled = true,
                    TargetChannelId = "chan-1",
                    PingRoleId = roleId,
                    ConfigSchemaVersion = 1,
                }
            );
            await db.SaveChangesAsync();
        }

        await using (DiscordTestDbContext db = database.NewContext())
            (await NewService(db, new RecordingGateway()).DeleteRoleAsync(channel, roleId))
                .IsSuccess.Should()
                .BeTrue();

        // The config's ping role is nulled (the FK is nullable), the config itself survives.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationConfig config = await db.DiscordNotificationConfigs.SingleAsync(c =>
                c.Id == configId
            );
            config.PingRoleId.Should().BeNull();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordNotificationRoleService NewService(
        DiscordTestDbContext db,
        RecordingGateway gateway,
        RecordingEventBus? bus = null
    ) => new(db, new DiscordTestUnitOfWork(db), gateway, bus ?? new RecordingEventBus(), Clock);

    private static async Task<Guid> SeedChannelAsync(DiscordSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Guid> SeedConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        string guildId = "guild-1"
    )
    {
        Guid id = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = id,
                BroadcasterId = channel,
                GuildId = guildId,
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
                BotInstalled = true,
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedRoleAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        Guid connectionId,
        string discordRoleId
    )
    {
        Guid id = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordNotificationRoles.Add(
            new DiscordNotificationRole
            {
                Id = id,
                BroadcasterId = channel,
                GuildConnectionId = connectionId,
                DiscordRoleId = discordRoleId,
                RoleName = "Squad",
                SelfAssignEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return id;
    }
}
