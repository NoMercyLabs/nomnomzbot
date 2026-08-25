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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Infrastructure.Discord;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the guild directory (the dashboard's role/channel pickers): the tenant's connection row
/// resolves to its GuildId before the gateway read; an absent or other-tenant connection is NOT_FOUND and never
/// reaches the gateway; gateway results flow through unchanged.
/// </summary>
public sealed class DiscordGuildDirectoryServiceTests
{
    [Fact]
    public async Task GetGuildRolesAsync_ResolvesGuildIdFromConnection_AndReturnsGatewayRoles()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new()
        {
            NextGuildRolesResult = Result.Success<IReadOnlyList<DiscordGuildRoleDto>>([
                new("role-1", "Notify Squad", 0xFF00FF, 3, false),
            ]),
        };

        await using DiscordTestDbContext db = database.NewContext();
        Result<IReadOnlyList<DiscordGuildRoleDto>> result = await new DiscordGuildDirectoryService(
            db,
            gateway
        ).GetGuildRolesAsync(channel, connectionId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().ContainSingle();
        result
            .Value[0]
            .Should()
            .Be(new DiscordGuildRoleDto("role-1", "Notify Squad", 0xFF00FF, 3, false));
        gateway.GuildReads.Should().Equal("roles:guild-777"); // resolved from the connection row
    }

    [Fact]
    public async Task GetGuildAsync_And_Channels_ProxyThroughTheConnectionsGuildId()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new()
        {
            NextGuildResult = Result.Success(
                new DiscordGuildInfoDto("guild-777", "The Guild", "iconhash", "About us")
            ),
            NextGuildChannelsResult = Result.Success<IReadOnlyList<DiscordGuildChannelDto>>([
                new("chan-1", "general", 0, "cat-1", 2),
            ]),
        };

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<DiscordGuildInfoDto> guild = await service.GetGuildAsync(channel, connectionId);
        Result<IReadOnlyList<DiscordGuildChannelDto>> channels =
            await service.GetGuildChannelsAsync(channel, connectionId);

        guild.IsSuccess.Should().BeTrue(guild.ErrorMessage);
        guild
            .Value.Should()
            .Be(new DiscordGuildInfoDto("guild-777", "The Guild", "iconhash", "About us"));
        channels.IsSuccess.Should().BeTrue(channels.ErrorMessage);
        channels.Value.Should().ContainSingle();
        channels
            .Value[0]
            .Should()
            .Be(new DiscordGuildChannelDto("chan-1", "general", 0, "cat-1", 2));
        gateway.GuildReads.Should().Equal("guild:guild-777", "channels:guild-777");
    }

    [Fact]
    public async Task OtherTenantOrAbsentConnection_IsNotFound_AndNeverReachesTheGateway()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (_, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<DiscordGuildInfoDto> otherTenant = await service.GetGuildAsync(
            Guid.CreateVersion7(),
            connectionId
        );
        Result<IReadOnlyList<DiscordGuildRoleDto>> absent = await service.GetGuildRolesAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        );

        otherTenant.IsFailure.Should().BeTrue();
        otherTenant.ErrorCode.Should().Be("NOT_FOUND");
        absent.IsFailure.Should().BeTrue();
        absent.ErrorCode.Should().Be("NOT_FOUND");
        gateway.GuildReads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_And_PostableChannels_ProxyThroughTheConnectionsGuildId()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new()
        {
            NextAssignableRolesResult = Result.Success<IReadOnlyList<DiscordAssignableRoleDto>>([
                new("role-1", "Live", 0, 2, false, false, "0", true, null, null),
            ]),
            NextPostableChannelsResult = Result.Success<IReadOnlyList<DiscordPostableChannelDto>>([
                new("chan-1", "general", 0, null, 0, true, null, null),
            ]),
        };

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> roles =
            await service.GetAssignableGuildRolesAsync(channel, connectionId);
        Result<IReadOnlyList<DiscordPostableChannelDto>> channels =
            await service.GetPostableGuildChannelsAsync(channel, connectionId);

        roles.IsSuccess.Should().BeTrue(roles.ErrorMessage);
        roles.Value.Should().ContainSingle().Which.CanAssign.Should().BeTrue();
        channels.IsSuccess.Should().BeTrue(channels.ErrorMessage);
        channels.Value.Should().ContainSingle().Which.CanPost.Should().BeTrue();
        gateway
            .GuildReads.Should()
            .Equal("assignable-roles:guild-777", "postable-channels:guild-777");
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_InactiveLink_FailsDistinctly_AndNeverReachesTheGateway()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connectionId) = await SeedAsync(
            database,
            guildId: "guild-777",
            streamerEnabled: false
        );
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> result =
            await service.GetAssignableGuildRolesAsync(channel, connectionId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_LINK_INACTIVE"); // distinct from NOT_FOUND and from an empty list
        gateway.GuildReads.Should().BeEmpty(); // never reached the unreachable/inactive guild over the wire
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_OtherTenantConnection_IsNotFound_AndNeverReachesTheGateway()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (_, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> otherTenant =
            await service.GetAssignableGuildRolesAsync(Guid.CreateVersion7(), connectionId);
        Result<IReadOnlyList<DiscordPostableChannelDto>> absent =
            await service.GetPostableGuildChannelsAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7()
            );

        otherTenant.IsFailure.Should().BeTrue();
        otherTenant.ErrorCode.Should().Be("NOT_FOUND");
        absent.IsFailure.Should().BeTrue();
        absent.ErrorCode.Should().Be("NOT_FOUND");
        gateway.GuildReads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_ActiveLinkWithNoRoles_ReturnsEmptyList_NotFailure()
    {
        // Proves the third state is distinct from the other two: a reachable, active link with nothing to show
        // returns Success([]) — never DISCORD_LINK_INACTIVE, never NOT_FOUND.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, Guid connectionId) = await SeedAsync(database, guildId: "guild-777");
        RecordingGateway gateway = new()
        {
            NextAssignableRolesResult = Result.Success<IReadOnlyList<DiscordAssignableRoleDto>>([]),
        };

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildDirectoryService service = new(db, gateway);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> result =
            await service.GetAssignableGuildRolesAsync(channel, connectionId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().BeEmpty();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<(Guid Channel, Guid ConnectionId)> SeedAsync(
        DiscordSqliteTestDatabase database,
        string guildId,
        bool streamerEnabled = true
    )
    {
        Guid channelId = Guid.CreateVersion7();
        Guid connectionId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        db.DiscordGuildConnections.Add(
            new()
            {
                Id = connectionId,
                BroadcasterId = channelId,
                GuildId = guildId,
                ServerConsentStatus = "approved",
                StreamerEnabled = streamerEnabled,
                BotInstalled = true,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, connectionId);
    }
}
