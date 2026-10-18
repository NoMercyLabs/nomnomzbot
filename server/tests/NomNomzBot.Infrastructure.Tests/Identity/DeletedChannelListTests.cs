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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A soft-deleted channel inside its restore window is listed for its owner with the delete date and the restore
/// deadline, and every join, leave, delete and restore tells the channel's open dashboards through ConfigChanged.
/// </summary>
public sealed class DeletedChannelListTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_deleted_channel_lists_for_its_owner_with_the_delete_date_and_the_restore_deadline()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        RecordingEventBus bus = new();
        (Guid ownerId, Guid channelId) = await SeedChannelAsync(db, bus);
        ChannelService service = Build(db, bus);
        (await service.DeleteAsync(channelId.ToString())).IsSuccess.Should().BeTrue();

        Result<PagedList<ChannelSummaryDto>> deleted = await service.GetDeletedChannelsAsync(
            ownerId.ToString(),
            new(1, 50, null, null)
        );

        deleted.IsSuccess.Should().BeTrue();
        ChannelSummaryDto row = deleted.Value.Items.Should().ContainSingle().Subject;
        row.Id.Should().Be(channelId.ToString());
        row.Login.Should().Be("streamer");
        row.DeletedAt.Should().Be(Now);
        row.PermanentAfter.Should().Be(ChannelDeletionPolicy.PermanentAfter(Now));
    }

    [Fact]
    public async Task A_channel_past_its_restore_window_and_a_live_channel_are_not_listed()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        RecordingEventBus bus = new();
        (Guid ownerId, Guid channelId) = await SeedChannelAsync(db, bus);
        ChannelService service = Build(db, bus);

        (await service.GetDeletedChannelsAsync(ownerId.ToString(), new(1, 50, null, null)))
            .Value.Items.Should()
            .BeEmpty();

        Channel channel = await db.Channels.SingleAsync(c => c.Id == channelId);
        channel.DeletedAt = Now.AddDays(-ChannelDeletionPolicy.RestoreWindowDays - 1);
        await db.SaveChangesAsync();

        (await service.GetDeletedChannelsAsync(ownerId.ToString(), new(1, 50, null, null)))
            .Value.Items.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task Another_user_never_sees_a_deleted_channel_they_do_not_own()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        RecordingEventBus bus = new();
        (Guid _, Guid channelId) = await SeedChannelAsync(db, bus);
        ChannelService service = Build(db, bus);
        await service.DeleteAsync(channelId.ToString());

        Result<PagedList<ChannelSummaryDto>> other = await service.GetDeletedChannelsAsync(
            Guid.NewGuid().ToString(),
            new(1, 50, null, null)
        );

        other.Value.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("join", "joined")]
    [InlineData("leave", "left")]
    [InlineData("delete", "deleted")]
    [InlineData("restore", "restored")]
    public async Task Every_presence_change_publishes_one_channels_ConfigChanged_for_that_channel(
        string operation,
        string action
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        RecordingEventBus bus = new();
        (Guid _, Guid channelId) = await SeedChannelAsync(db, bus);
        ChannelService service = Build(db, bus);
        if (operation == "restore")
            await service.DeleteAsync(channelId.ToString());
        bus.Published.Clear();

        Result result = operation switch
        {
            "join" => await service.JoinAsync(channelId.ToString()),
            "leave" => await service.LeaveAsync(channelId.ToString()),
            "delete" => await service.DeleteAsync(channelId.ToString()),
            _ => await service.RestoreAsync(channelId.ToString()),
        };

        result.IsSuccess.Should().BeTrue();
        ChannelConfigChangedEvent signal = bus
            .Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        signal.BroadcasterId.Should().Be(channelId);
        signal.Domain.Should().Be("channels");
        signal.EntityId.Should().Be(channelId.ToString());
        signal.Action.Should().Be(action);
    }

    private static async Task<(Guid OwnerId, Guid ChannelId)> SeedChannelAsync(
        AuthDbContext db,
        RecordingEventBus bus
    )
    {
        User owner = new()
        {
            TwitchUserId = "tw-400",
            Username = "streamer",
            UsernameNormalized = "streamer",
            DisplayName = "Streamer",
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        Result<Guid> tenant = await Build(db, bus)
            .EnsureModeratedTenantAsync("tw-400", "streamer", "Streamer", owner.Id);
        tenant.IsSuccess.Should().BeTrue();
        return (owner.Id, tenant.Value);
    }

    private static ChannelService Build(AuthDbContext db, RecordingEventBus bus)
    {
        TimeProvider clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(new DateTimeOffset(Now));
        return new(
            db,
            clock,
            bus,
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchEventSubService>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<IBotModeratorStatusService>(),
            new SupportContactService(db, new ConfigurationBuilder().Build())
        );
    }
}
