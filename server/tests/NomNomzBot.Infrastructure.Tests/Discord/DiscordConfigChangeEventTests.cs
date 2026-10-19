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
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// A Discord live-role or notification-rule write tells every open dashboard of the channel to refetch:
/// one <see cref="ChannelConfigChangedEvent"/> in the "discord" domain, carrying the row id and the action.
/// A refused write publishes nothing.
/// </summary>
public sealed class DiscordConfigChangeEventTests
{
    [Fact]
    public async Task Live_role_create_update_and_delete_each_publish_one_discord_event_with_the_row_id()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        IEventBus eventBus = Substitute.For<IEventBus>();

        Guid rowId;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordLiveRoleConfigDto> created = await DiscordConfigDefaultsTests
                .NewLiveRoleService(db, eventBus)
                .CreateAsync(channel, connectionId, new("role-1", "member-1"));
            created.IsSuccess.Should().BeTrue(created.ErrorMessage);
            rowId = created.Value.Id;
        }
        await AssertPublishedAsync(eventBus, channel, rowId, "created");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordLiveRoleConfigDto> updated = await DiscordConfigDefaultsTests
                .NewLiveRoleService(db, eventBus)
                .UpdateAsync(channel, connectionId, new("role-2", "member-2", true));
            updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        }
        await AssertPublishedAsync(eventBus, channel, rowId, "updated");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result deleted = await DiscordConfigDefaultsTests
                .NewLiveRoleService(db, eventBus)
                .DeleteAsync(channel, connectionId);
            deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        }
        await AssertPublishedAsync(eventBus, channel, rowId, "deleted");
    }

    [Fact]
    public async Task A_refused_live_role_write_publishes_nothing()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        IEventBus eventBus = Substitute.For<IEventBus>();

        await using DiscordTestDbContext db = database.NewContext();
        Result<DiscordLiveRoleConfigDto> refused = await DiscordConfigDefaultsTests
            .NewLiveRoleService(db, eventBus)
            .UpdateAsync(channel, connectionId, new("role-1", "member-1", true));

        refused.IsFailure.Should().BeTrue();
        await eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notification_rule_create_update_and_delete_each_publish_one_discord_event_with_the_row_id()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        IEventBus eventBus = Substitute.For<IEventBus>();

        Guid ruleId;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationConfigDto> created = await DiscordConfigDefaultsTests
                .NewConfigService(db, eventBus)
                .CreateConfigAsync(
                    channel,
                    connectionId,
                    new("go_live", "chan-1", null, "live now", null, null, null, true)
                );
            created.IsSuccess.Should().BeTrue(created.ErrorMessage);
            ruleId = created.Value.Id;
        }
        await AssertPublishedAsync(eventBus, channel, ruleId, "created");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationConfigDto> updated = await DiscordConfigDefaultsTests
                .NewConfigService(db, eventBus)
                .UpdateConfigAsync(
                    channel,
                    ruleId,
                    new(false, "chan-2", null, "still live", null, null, null)
                );
            updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        }
        await AssertPublishedAsync(eventBus, channel, ruleId, "updated");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result deleted = await DiscordConfigDefaultsTests
                .NewConfigService(db, eventBus)
                .DeleteConfigAsync(channel, ruleId);
            deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        }
        await AssertPublishedAsync(eventBus, channel, ruleId, "deleted");
    }

    [Fact]
    public async Task A_refused_notification_rule_write_publishes_nothing()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        IEventBus eventBus = Substitute.For<IEventBus>();

        await using DiscordTestDbContext db = database.NewContext();
        Result deleted = await DiscordConfigDefaultsTests
            .NewConfigService(db, eventBus)
            .DeleteConfigAsync(channel, Guid.NewGuid());

        deleted.IsFailure.Should().BeTrue();
        await eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }

    private static async Task AssertPublishedAsync(
        IEventBus eventBus,
        Guid channel,
        Guid rowId,
        string action
    ) =>
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == channel
                    && e.Domain == "discord"
                    && e.EntityId == rowId.ToString()
                    && e.Action == action
                ),
                Arg.Any<CancellationToken>()
            );
}
