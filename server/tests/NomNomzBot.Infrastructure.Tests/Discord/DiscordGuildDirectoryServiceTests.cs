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
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
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
                new DiscordGuildRoleDto("role-1", "Notify Squad", 0xFF00FF, 3, false),
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
                new DiscordGuildChannelDto("chan-1", "general", 0, "cat-1", 2),
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

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<(Guid Channel, Guid ConnectionId)> SeedAsync(
        DiscordSqliteTestDatabase database,
        string guildId
    )
    {
        Guid channelId = Guid.CreateVersion7();
        Guid connectionId = Guid.CreateVersion7();
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
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = connectionId,
                BroadcasterId = channelId,
                GuildId = guildId,
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
                BotInstalled = true,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, connectionId);
    }
}
