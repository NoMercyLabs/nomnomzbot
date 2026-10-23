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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// The live role goes to the streamer's own Discord account: the one Discord has linked to their Twitch account,
/// recorded when the bot was added. Nobody picks a member from a list; a request that names none uses that account.
/// </summary>
public sealed class DiscordLiveRoleConfigServiceTests
{
    [Fact]
    public async Task CreateAsync_WithNoMember_GivesTheRoleToTheStreamersLinkedDiscordAccount()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        Result<DiscordLiveRoleConfigDto> created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            created = await NewService(db).CreateAsync(channel, connection, new("role-1"));
        }

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        created.Value.DiscordMemberId.Should().Be("streamer-discord-id");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordLiveRoleConfig stored = await db.DiscordLiveRoleConfigs.SingleAsync();
            stored.DiscordMemberId.Should().Be("streamer-discord-id");
            stored.RoleId.Should().Be("role-1");
        }
    }

    [Fact]
    public async Task CreateAsync_WithNoMemberAndNoLinkedAccount_SaysToLinkTwitchOnDiscord()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, installedBy: null);

        Result<DiscordLiveRoleConfigDto> created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            created = await NewService(db).CreateAsync(channel, connection, new("role-1"));
        }

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("LIVE_ROLE_NEEDS_TWITCH_LINK");
        created.ErrorMessage.Should().Contain("Connections").And.Contain("Twitch");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            (await db.DiscordLiveRoleConfigs.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNoMember_KeepsTheRoleOnTheStreamersAccount()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewService(db).CreateAsync(channel, connection, new("role-1"));
        }

        Result<DiscordLiveRoleConfigDto> updated;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            updated = await NewService(db)
                .UpdateAsync(channel, connection, new("role-2", null, Enabled: false));
        }

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated.Value.RoleId.Should().Be("role-2");
        updated.Value.DiscordMemberId.Should().Be("streamer-discord-id");
        updated.Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_WithAnExplicitMember_StillUsesIt()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        Result<DiscordLiveRoleConfigDto> created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            created = await NewService(db)
                .CreateAsync(channel, connection, new("role-1", "someone-else"));
        }

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        created.Value.DiscordMemberId.Should().Be("someone-else");
    }

    // A removed rule keeps its row (soft delete) and the unique index still counts it: setting the rule up again
    // used to crash with a duplicate key. It brings the same row back instead.
    [Fact]
    public async Task CreateAsync_AfterTheRuleWasRemoved_BringsTheSameRowBack()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewService(db).CreateAsync(channel, connection, new("role-1"));
            await NewService(db).DeleteAsync(channel, connection);
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordLiveRoleConfig removed = await db
                .DiscordLiveRoleConfigs.IgnoreQueryFilters()
                .SingleAsync();
            removed
                .DeletedAt.Should()
                .NotBeNull("the premise: a removed rule leaves its row behind");
        }

        Result<DiscordLiveRoleConfigDto> again;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            again = await NewService(db).CreateAsync(channel, connection, new("role-2"));
        }

        again.IsSuccess.Should().BeTrue(again.ErrorMessage);

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordLiveRoleConfig stored = await db
                .DiscordLiveRoleConfigs.IgnoreQueryFilters()
                .SingleAsync();
            stored.DeletedAt.Should().BeNull();
            stored.RoleId.Should().Be("role-2");
            stored.DiscordMemberId.Should().Be("streamer-discord-id");
            stored.IsCurrentlyApplied.Should().BeFalse();
        }
    }

    // One Discord role serves one purpose: the role the streamer wears while live is never also a role viewers pick
    // for pings, so the same role cannot show up twice on the page.
    [Fact]
    public async Task CreateAsync_ForARoleViewersAlreadyPick_IsRefused()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewRoleService(db)
                .CreateRoleAsync(channel, connection, new("role-1", "Pings", true));
        }

        Result<DiscordLiveRoleConfigDto> created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            created = await NewService(db).CreateAsync(channel, connection, new("role-1"));
        }

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("ROLE_IN_USE_AS_SELF_ASSIGN");
    }

    [Fact]
    public async Task CreateRoleAsync_ForTheLiveRole_IsRefused()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connection) = await SeedLinkAsync(database, "streamer-discord-id");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewService(db).CreateAsync(channel, connection, new("role-1"));
        }

        Result<DiscordNotificationRoleDto> created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            created = await NewRoleService(db)
                .CreateRoleAsync(channel, connection, new("role-1", "Currently live", true));
        }

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("ROLE_IN_USE_AS_LIVE_ROLE");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            (await db.DiscordNotificationRoles.AnyAsync()).Should().BeFalse();
        }
    }

    private static DiscordNotificationRoleService NewRoleService(DiscordTestDbContext db) =>
        new(
            db,
            new DiscordTestUnitOfWork(db),
            new RecordingGateway(),
            new RecordingEventBus(),
            TimeProvider.System
        );

    private static DiscordLiveRoleConfigService NewService(DiscordTestDbContext db) =>
        new(db, new DiscordTestUnitOfWork(db), new RecordingEventBus());

    private static async Task<(Guid Channel, Guid Connection)> SeedLinkAsync(
        DiscordSqliteTestDatabase database,
        string? installedBy
    )
    {
        Guid channel = Guid.CreateVersion7();
        Guid connection = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channel,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        db.DiscordGuildConnections.Add(
            new()
            {
                Id = connection,
                BroadcasterId = channel,
                GuildId = "guild-1",
                GuildName = "Server",
                BotInstalled = true,
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
                ApprovedByDiscordUserId = installedBy,
            }
        );
        await db.SaveChangesAsync();
        return (channel, connection);
    }
}
