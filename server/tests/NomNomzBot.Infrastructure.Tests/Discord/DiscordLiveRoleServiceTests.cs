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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the "currently live" Discord role rule (discord.md live-role extension). Each proves a
/// consequence — the actual Discord add/remove call the gateway received and its arguments, the tenant
/// isolation gate (a channel with no ACTIVE both-opt-in link into the guild never drives a role there), the
/// idempotent duplicate-online no-op, and the startup self-heal of a role stranded by a missed offline event.
/// </summary>
public sealed class DiscordLiveRoleServiceTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero));
    private const string DedupeKey = "live_role:2026-06-22T12:00:00.0000000Z";

    [Fact]
    public async Task ApplyForOnlineAsync_AddsTheConfiguredRole_OnTheStreamersOwnMember()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid owner = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, owner, "guild-1");
        await SeedLiveRoleConfigAsync(database, owner, connectionId, "role-live", "member-owner");

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ApplyForOnlineAsync(owner, DedupeKey);

        gateway
            .RoleAdds.Should()
            .ContainSingle(a =>
                a.GuildId == "guild-1" && a.MemberId == "member-owner" && a.RoleId == "role-live"
            );

        (
            await database
                .NewContext()
                .DiscordLiveRoleConfigs.FindAsync(await FirstConfigIdAsync(database, owner))
        )!
            .IsCurrentlyApplied.Should()
            .BeTrue();
    }

    [Fact]
    public async Task ApplyForOnlineAsync_DuplicateOnlineEvent_DoesNotDoubleApply()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid owner = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, owner, "guild-1");
        await SeedLiveRoleConfigAsync(database, owner, connectionId, "role-live", "member-owner");

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ApplyForOnlineAsync(owner, DedupeKey);
        await service.ApplyForOnlineAsync(owner, DedupeKey); // duplicate stream.online for the SAME session

        gateway
            .RoleAdds.Should()
            .HaveCount(1, "a repeat online event for the same session must not re-add the role");
    }

    [Fact]
    public async Task RemoveForOfflineAsync_RemovesTheRole_AndClearsAppliedState()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid owner = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, owner, "guild-1");
        await SeedLiveRoleConfigAsync(
            database,
            owner,
            connectionId,
            "role-live",
            "member-owner",
            isCurrentlyApplied: true
        );

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.RemoveForOfflineAsync(owner);

        gateway
            .RoleRemoves.Should()
            .ContainSingle(r =>
                r.GuildId == "guild-1" && r.MemberId == "member-owner" && r.RoleId == "role-live"
            );

        await using DiscordTestDbContext verify = database.NewContext();
        DiscordLiveRoleConfig config = await verify.DiscordLiveRoleConfigs.SingleAsync(c =>
            c.BroadcasterId == owner
        );
        config.IsCurrentlyApplied.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyForOnlineAsync_UnlinkedFriendChannel_NeverAppliesTheRole()
    {
        // The friend's channel HAS a DiscordLiveRoleConfig row pointed at the owner's guild, but the guild
        // connection was never approved by the guild admin — no active both-opt-in link, so tenant isolation
        // must block the role add entirely.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid friend = await SeedChannelAsync(database);
        Guid connectionId = await SeedPendingConnectionAsync(database, friend, "guild-1");
        await SeedLiveRoleConfigAsync(database, friend, connectionId, "role-live", "member-friend");

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ApplyForOnlineAsync(friend, DedupeKey);

        gateway
            .RoleAdds.Should()
            .BeEmpty("an unlinked channel must never drive a role in someone else's guild");
    }

    [Fact]
    public async Task ApplyForOnlineAsync_LinkedFriendChannel_AppliesTheRole_InTheOwnersGuild()
    {
        // The friend accepted their OWN both-opt-in link into the owner's guild (a separate
        // DiscordGuildConnection row, tenant-scoped to the friend) — the explicit consent that lets their
        // channel drive a role in that guild.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid friend = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, friend, "guild-1");
        await SeedLiveRoleConfigAsync(database, friend, connectionId, "role-live", "member-friend");

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ApplyForOnlineAsync(friend, DedupeKey);

        gateway
            .RoleAdds.Should()
            .ContainSingle(a =>
                a.GuildId == "guild-1" && a.MemberId == "member-friend" && a.RoleId == "role-live"
            );
    }

    [Fact]
    public async Task ReconcileStaleAsync_ClearsARoleStrandedByAMissedOfflineEvent()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid owner = await SeedChannelAsync(database, isLive: false); // the channel is actually offline now
        Guid connectionId = await SeedActiveConnectionAsync(database, owner, "guild-1");
        await SeedLiveRoleConfigAsync(
            database,
            owner,
            connectionId,
            "role-live",
            "member-owner",
            isCurrentlyApplied: true // stranded: still marked applied despite the missed offline event
        );

        RecordingGateway gateway = new();
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ReconcileStaleAsync();

        gateway
            .RoleRemoves.Should()
            .ContainSingle(r =>
                r.GuildId == "guild-1" && r.MemberId == "member-owner" && r.RoleId == "role-live"
            );

        await using DiscordTestDbContext verify = database.NewContext();
        DiscordLiveRoleConfig config = await verify.DiscordLiveRoleConfigs.SingleAsync(c =>
            c.BroadcasterId == owner
        );
        config.IsCurrentlyApplied.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyForOnlineAsync_ValidationFailure_NeverCallsAddRole_ButIsRecorded()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid owner = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, owner, "guild-1");
        await SeedLiveRoleConfigAsync(database, owner, connectionId, "role-live", "member-owner");

        RecordingGateway gateway = new();
        gateway.ValidationResultsByRole["role-live"] = Result.Failure(
            "The bot needs the Manage Roles permission in this Discord server to manage the live role.",
            "DISCORD_MISSING_MANAGE_ROLES"
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleService service = NewService(db, gateway);

        await service.ApplyForOnlineAsync(owner, DedupeKey);

        gateway
            .ValidationChecks.Should()
            .ContainSingle(c => c.GuildId == "guild-1" && c.RoleId == "role-live");
        gateway
            .RoleAdds.Should()
            .BeEmpty("a failed hierarchy/permission check must block the add call");
    }

    private static DiscordLiveRoleService NewService(
        DiscordTestDbContext db,
        RecordingGateway gateway
    ) =>
        new(
            db,
            new DiscordGuildService(
                db,
                new RecordingVault(),
                new DiscordTestUnitOfWork(db),
                new RecordingEventBus(),
                Clock
            ),
            gateway,
            NullLogger<DiscordLiveRoleService>.Instance
        );

    private static async Task<Guid> SeedChannelAsync(
        DiscordSqliteTestDatabase database,
        bool isLive = true
    )
    {
        Guid channelId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = Guid.NewGuid().ToString("N")[..12],
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                IsLive = isLive,
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Guid> SeedActiveConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid broadcasterId,
        string guildId
    )
    {
        Guid connectionId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordGuildConnections.Add(
            new()
            {
                Id = connectionId,
                BroadcasterId = broadcasterId,
                GuildId = guildId,
                BotInstalled = true,
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return connectionId;
    }

    private static async Task<Guid> SeedPendingConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid broadcasterId,
        string guildId
    )
    {
        Guid connectionId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordGuildConnections.Add(
            new()
            {
                Id = connectionId,
                BroadcasterId = broadcasterId,
                GuildId = guildId,
                BotInstalled = true,
                ServerConsentStatus = "pending", // guild admin never approved — no active link
                StreamerEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return connectionId;
    }

    private static async Task SeedLiveRoleConfigAsync(
        DiscordSqliteTestDatabase database,
        Guid broadcasterId,
        Guid connectionId,
        string roleId,
        string discordMemberId,
        bool isCurrentlyApplied = false
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordLiveRoleConfigs.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                GuildConnectionId = connectionId,
                RoleId = roleId,
                DiscordMemberId = discordMemberId,
                Enabled = true,
                IsCurrentlyApplied = isCurrentlyApplied,
                AppliedDedupeKey = isCurrentlyApplied ? DedupeKey : null,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> FirstConfigIdAsync(
        DiscordSqliteTestDatabase database,
        Guid broadcasterId
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleConfig config = await db.DiscordLiveRoleConfigs.SingleAsync(c =>
            c.BroadcasterId == broadcasterId
        );
        return config.Id;
    }
}
