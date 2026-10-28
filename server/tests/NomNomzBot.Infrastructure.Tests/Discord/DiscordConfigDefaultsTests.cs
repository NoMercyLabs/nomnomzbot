// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Discord;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Every Discord setting has one default (<see cref="DiscordConfigDefaults"/>). A channel with no rule reads
/// an empty list (no rule, no post), a new entity and a create request without the field carry the same
/// default, and every settable field is saved and read back through the services.
/// </summary>
public sealed class DiscordConfigDefaultsTests
{
    [Fact]
    public async Task A_channel_with_no_rules_reads_empty_lists_and_a_read_writes_nothing()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using DiscordTestDbContext db = database.NewContext();
        IReadOnlyList<DiscordNotificationConfigDto> rules = (
            await NewConfigService(db).GetConfigsAsync(channel, connectionId)
        ).Value;
        IReadOnlyList<DiscordLiveRoleConfigDto> liveRoles = (
            await NewLiveRoleService(db).GetAsync(channel, connectionId)
        ).Value;

        rules.Should().BeEmpty();
        liveRoles.Should().BeEmpty();
        (await db.DiscordNotificationConfigs.CountAsync()).Should().Be(0);
        (await db.DiscordLiveRoleConfigs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void New_entities_carry_the_single_source_defaults()
    {
        DiscordNotificationConfig rule = new();
        DiscordLiveRoleConfig liveRole = new();

        rule.Enabled.Should().Be(DiscordConfigDefaults.NotificationEnabled);
        rule.ConfigSchemaVersion.Should().Be(DiscordConfigDefaults.EmbedConfigVersion);
        liveRole.Enabled.Should().Be(DiscordConfigDefaults.LiveRoleEnabled);
    }

    [Fact]
    public async Task A_create_request_without_enabled_stores_the_default()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using DiscordTestDbContext db = database.NewContext();
        DiscordNotificationConfigDto created = (
            await NewConfigService(db)
                .CreateConfigAsync(
                    channel,
                    connectionId,
                    new("go_live", "1234", null, "hi", null, null, null)
                )
        ).Value;

        created.Enabled.Should().Be(DiscordConfigDefaults.NotificationEnabled);
        DiscordNotificationConfig stored = await db.DiscordNotificationConfigs.SingleAsync();
        stored.ConfigSchemaVersion.Should().Be(DiscordConfigDefaults.EmbedConfigVersion);
    }

    [Fact]
    public async Task Every_notification_field_is_saved_and_read_back_changed()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        Guid configId = await DiscordTestHarness.SeedConfigAsync(
            database,
            channel,
            connectionId,
            "old",
            "111",
            triggerType: "milestone"
        );
        DiscordEmbedDto embed = new(
            "T",
            "D",
            "#fff",
            "https://t/a.png",
            "https://t/b.png",
            "F",
            [new("n", "v", true)]
        );

        await using DiscordTestDbContext db = database.NewContext();
        await NewConfigService(db)
            .UpdateConfigAsync(
                channel,
                configId,
                new(
                    !DiscordConfigDefaults.NotificationEnabled,
                    "222",
                    null,
                    "new",
                    embed,
                    "followers",
                    500
                )
            );

        await using DiscordTestDbContext fresh = database.NewContext();
        DiscordNotificationConfigDto read = (
            await NewConfigService(fresh).GetConfigsAsync(channel, connectionId)
        ).Value.Single();
        read.Enabled.Should().Be(!DiscordConfigDefaults.NotificationEnabled);
        read.TargetChannelId.Should().Be("222");
        read.MessageTemplate.Should().Be("new");
        read.EmbedConfig.Should().BeEquivalentTo(embed);
        read.MilestoneType.Should().Be("followers");
        read.MilestoneThreshold.Should().Be(500);
    }

    [Fact]
    public async Task Every_live_role_field_is_saved_and_read_back_changed()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordLiveRoleConfigDto created = (
                await NewLiveRoleService(db)
                    .CreateAsync(channel, connectionId, new("role-1", "member-1"))
            ).Value;
            created.Enabled.Should().Be(DiscordConfigDefaults.LiveRoleEnabled);
            created.IsCurrentlyApplied.Should().BeFalse();
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewLiveRoleService(db)
                .UpdateAsync(
                    channel,
                    connectionId,
                    new("role-2", "member-2", !DiscordConfigDefaults.LiveRoleEnabled)
                );
        }

        await using DiscordTestDbContext fresh = database.NewContext();
        DiscordLiveRoleConfigDto read = (
            await NewLiveRoleService(fresh).GetAsync(channel, connectionId)
        ).Value.Single();
        read.RoleId.Should().Be("role-2");
        read.DiscordMemberId.Should().Be("member-2");
        read.Enabled.Should().Be(!DiscordConfigDefaults.LiveRoleEnabled);
    }

    [Fact]
    public async Task A_live_role_in_use_cannot_change_its_role_or_be_deleted()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        await using DiscordTestDbContext db = database.NewContext();
        DiscordLiveRoleConfigService service = NewLiveRoleService(db);
        await service.CreateAsync(channel, connectionId, new("role-1", "member-1"));
        DiscordLiveRoleConfig row = await db.DiscordLiveRoleConfigs.SingleAsync();
        row.IsCurrentlyApplied = true;
        await db.SaveChangesAsync();

        Result<DiscordLiveRoleConfigDto> changed = await service.UpdateAsync(
            channel,
            connectionId,
            new("role-9", "member-1", true)
        );
        Result deleted = await service.DeleteAsync(channel, connectionId);

        changed.IsFailure.Should().BeTrue();
        deleted.IsFailure.Should().BeTrue();
        (await db.DiscordLiveRoleConfigs.SingleAsync()).RoleId.Should().Be("role-1");
    }

    internal static DiscordNotificationConfigService NewConfigService(
        DiscordTestDbContext db,
        IEventBus? eventBus = null
    ) =>
        new(
            db,
            new DiscordTestUnitOfWork(db),
            DiscordTemplateTestSupport.CreateResolver(),
            DiscordTemplateTestSupport.CreateValidator(),
            eventBus ?? Substitute.For<IEventBus>()
        );

    internal static DiscordLiveRoleConfigService NewLiveRoleService(
        DiscordTestDbContext db,
        IEventBus? eventBus = null
    ) => new(db, new DiscordTestUnitOfWork(db), eventBus ?? Substitute.For<IEventBus>());
}
