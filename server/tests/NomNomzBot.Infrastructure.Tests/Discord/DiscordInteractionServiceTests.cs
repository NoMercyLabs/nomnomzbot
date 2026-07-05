// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Discord.Interactions;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the interaction router over the REAL notify-role service and SQLite harness: the PING
/// handshake answers the exact PONG JSON; a button click with <c>custom_id notify_optin:{roleId:N}</c> records
/// the opt-in row, pushes the Discord role through the gateway, publishes the opt-in event, and answers an
/// ephemeral type-4 confirmation; a second click toggles back off; disabled self-assign / unknown custom_ids
/// never touch state; a malformed body fails VALIDATION_FAILED.
/// </summary>
public sealed class DiscordInteractionServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero)
    );

    [Fact]
    public async Task Handle_Ping_ReturnsExactPongJson()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await using DiscordTestDbContext db = database.NewContext();

        Result<string> reply = await NewService(db, new RecordingGateway(), new RecordingEventBus())
            .HandleAsync("""{"type":1}""");

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        reply.Value.Should().Be("""{"type":1}""");
    }

    [Fact]
    public async Task Handle_OptInButton_RecordsRow_AssignsRole_PublishesEvent_AndConfirmsEphemeral()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (Guid channel, _, Guid roleId) = await SeedAsync(database);
        RecordingGateway gateway = new();
        RecordingEventBus bus = new();

        Result<string> reply;
        await using (DiscordTestDbContext db = database.NewContext())
            reply = await NewService(db, gateway, bus)
                .HandleAsync(ComponentPayload(roleId, memberId: "member-42"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);

        // The reply is an ephemeral CHANNEL_MESSAGE_WITH_SOURCE confirmation naming the role.
        using JsonDocument document = JsonDocument.Parse(reply.Value);
        document.RootElement.GetProperty("type").GetInt32().Should().Be(4);
        document.RootElement.GetProperty("data").GetProperty("flags").GetInt32().Should().Be(64);
        document
            .RootElement.GetProperty("data")
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("Squad")
            .And.Contain("added");

        // The opt-in row was recorded from the button (state change, not just a reply).
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordMemberOptIn stored = await db.DiscordMemberOptIns.SingleAsync();
            stored.BroadcasterId.Should().Be(channel);
            stored.NotificationRoleId.Should().Be(roleId);
            stored.DiscordMemberId.Should().Be("member-42");
            stored.OptInSource.Should().Be("button");
            stored.OptedOutAt.Should().BeNull();
        }

        // The Discord role was pushed into the right guild for the clicking member.
        gateway.RoleAdds.Should().ContainSingle();
        gateway.RoleAdds[0].Should().Be(("guild-btn", "member-42", "discord-role-7"));

        // The opt-in-changed event fired with source "button".
        DiscordMemberOptInChangedEvent ev = bus
            .Published.OfType<DiscordMemberOptInChangedEvent>()
            .Single();
        ev.OptedIn.Should().BeTrue();
        ev.Source.Should().Be("button");
    }

    [Fact]
    public async Task Handle_SecondClick_TogglesBackOff_RemovesRole_AndConfirmsEphemeral()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (_, _, Guid roleId) = await SeedAsync(database);
        RecordingGateway gateway = new();

        await using (DiscordTestDbContext db = database.NewContext())
            await NewService(db, gateway, new RecordingEventBus())
                .HandleAsync(ComponentPayload(roleId, "member-42"));

        RecordingEventBus bus = new();
        Result<string> reply;
        await using (DiscordTestDbContext db = database.NewContext())
            reply = await NewService(db, gateway, bus)
                .HandleAsync(ComponentPayload(roleId, "member-42"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(reply.Value);
        document.RootElement.GetProperty("type").GetInt32().Should().Be(4);
        document
            .RootElement.GetProperty("data")
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("removed");

        // The opt-in row is now opted out; the role removal went through the gateway.
        await using (DiscordTestDbContext db = database.NewContext())
            (await db.DiscordMemberOptIns.SingleAsync()).OptedOutAt.Should().NotBeNull();
        gateway.RoleRemoves.Should().ContainSingle();
        gateway.RoleRemoves[0].Should().Be(("guild-btn", "member-42", "discord-role-7"));
        bus.Published.OfType<DiscordMemberOptInChangedEvent>().Single().OptedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DmPayload_UsesTopLevelUserId()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (_, _, Guid roleId) = await SeedAsync(database);
        RecordingGateway gateway = new();

        string payload =
            """{"type":3,"data":{"custom_id":"notify_optin:ROLE_ID"},"user":{"id":"dm-user-9"}}""".Replace(
                "ROLE_ID",
                roleId.ToString("N")
            );
        await using DiscordTestDbContext db = database.NewContext();
        Result<string> reply = await NewService(db, gateway, new RecordingEventBus())
            .HandleAsync(payload);

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        gateway.RoleAdds.Should().ContainSingle();
        gateway.RoleAdds[0].MemberId.Should().Be("dm-user-9");
    }

    [Fact]
    public async Task Handle_SelfAssignDisabled_DoesNotToggle_AndSaysDisabled()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        (_, _, Guid roleId) = await SeedAsync(database, selfAssignEnabled: false);
        RecordingGateway gateway = new();

        Result<string> reply;
        await using (DiscordTestDbContext db = database.NewContext())
            reply = await NewService(db, gateway, new RecordingEventBus())
                .HandleAsync(ComponentPayload(roleId, "member-42"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(reply.Value);
        document.RootElement.GetProperty("type").GetInt32().Should().Be(4);
        document
            .RootElement.GetProperty("data")
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("disabled");

        await using (DiscordTestDbContext db = database.NewContext())
            (await db.DiscordMemberOptIns.CountAsync()).Should().Be(0);
        gateway.RoleAdds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        """{"type":3,"data":{"custom_id":"something_else:abc"},"member":{"user":{"id":"m1"}}}"""
    )]
    [InlineData(
        """{"type":3,"data":{"custom_id":"notify_optin:not-a-guid"},"member":{"user":{"id":"m1"}}}"""
    )]
    [InlineData("""{"type":3,"member":{"user":{"id":"m1"}}}""")]
    [InlineData("""{"type":2,"data":{"name":"some_command"}}""")]
    public async Task Handle_UnknownCustomIdOrType_RepliesUnsupported_WithoutStateChange(
        string payload
    )
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedAsync(database);
        RecordingGateway gateway = new();

        Result<string> reply;
        await using (DiscordTestDbContext db = database.NewContext())
            reply = await NewService(db, gateway, new RecordingEventBus()).HandleAsync(payload);

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(reply.Value);
        document.RootElement.GetProperty("type").GetInt32().Should().Be(4);
        document.RootElement.GetProperty("data").GetProperty("flags").GetInt32().Should().Be(64);

        await using (DiscordTestDbContext db = database.NewContext())
            (await db.DiscordMemberOptIns.CountAsync()).Should().Be(0);
        gateway.RoleAdds.Should().BeEmpty();
        gateway.RoleRemoves.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownRoleId_SaysButtonInactive_WithoutStateChange()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedAsync(database);
        RecordingGateway gateway = new();

        Result<string> reply;
        await using (DiscordTestDbContext db = database.NewContext())
            reply = await NewService(db, gateway, new RecordingEventBus())
                .HandleAsync(ComponentPayload(Guid.CreateVersion7(), "member-42"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(reply.Value);
        document
            .RootElement.GetProperty("data")
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("isn't active");
        gateway.RoleAdds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Handle_MalformedBody_FailsValidation(string body)
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await using DiscordTestDbContext db = database.NewContext();

        Result<string> reply = await NewService(db, new RecordingGateway(), new RecordingEventBus())
            .HandleAsync(body);

        reply.IsFailure.Should().BeTrue();
        reply.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordInteractionService NewService(
        DiscordTestDbContext db,
        RecordingGateway gateway,
        RecordingEventBus bus
    ) =>
        new(
            db,
            new DiscordNotificationRoleService(
                db,
                new DiscordTestUnitOfWork(db),
                gateway,
                bus,
                Clock
            ),
            NullLogger<DiscordInteractionService>.Instance
        );

    /// <summary>A realistic guild MESSAGE_COMPONENT click on the opt-in button (custom_id N-format Guid).</summary>
    private static string ComponentPayload(Guid roleId, string memberId) =>
        """{"type":3,"data":{"custom_id":"notify_optin:ROLE_ID","component_type":2},"guild_id":"guild-btn","member":{"user":{"id":"MEMBER_ID","username":"clicker"}},"token":"interaction-token","version":1}"""
            .Replace("ROLE_ID", roleId.ToString("N"))
            .Replace("MEMBER_ID", memberId);

    private static async Task<(Guid Channel, Guid ConnectionId, Guid RoleId)> SeedAsync(
        DiscordSqliteTestDatabase database,
        bool selfAssignEnabled = true
    )
    {
        Guid channelId = Guid.CreateVersion7();
        Guid connectionId = Guid.CreateVersion7();
        Guid roleId = Guid.CreateVersion7();
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
                GuildId = "guild-btn",
                ServerConsentStatus = "approved",
                StreamerEnabled = true,
                BotInstalled = true,
            }
        );
        db.DiscordNotificationRoles.Add(
            new DiscordNotificationRole
            {
                Id = roleId,
                BroadcasterId = channelId,
                GuildConnectionId = connectionId,
                DiscordRoleId = "discord-role-7",
                RoleName = "Squad",
                SelfAssignEnabled = selfAssignEnabled,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, connectionId, roleId);
    }
}
