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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Twitch's suspicious-user signals: the flag is stored per channel and chatter, a flagged chatter's message lands
/// in the moderation queue with its text (and is never deleted), a change pushes the "suspicious-users" domain, and
/// the viewer card's moderation context carries the flag.
/// </summary>
public sealed class SuspiciousUserTests
{
    private static readonly Guid Tenant = Guid.Parse("019f9b00-1111-7000-8000-000000000001");
    private const string ViewerTwitchId = "7007";

    private sealed class Rig
    {
        public required ModerationServiceTestDbContext Db { get; init; }
        public required IEventBus Bus { get; init; }
        public required ITwitchModerationApi Twitch { get; init; }
        public required LowTrustStatusService Statuses { get; init; }
        public required SuspiciousUserUpdatedHandler UpdatedHandler { get; init; }
        public required SuspiciousUserMessageHandler MessageHandler { get; init; }
    }

    private static async Task<Rig> BuildAsync()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "1001",
                ExternalChannelId = "1001",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        await db.SaveChangesAsync();

        IEventBus bus = Substitute.For<IEventBus>();
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Guid.NewGuid().ToString(),
                        "flagged",
                        "Flagged",
                        null,
                        null,
                        default,
                        default
                    )
                )
            );
        ModerationQueueService queue = new(
            db,
            users,
            twitch,
            Substitute.For<IModerationService>(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<ModerationQueueService>.Instance
        );
        LowTrustStatusService statuses = new(db, bus, TimeProvider.System);
        return new Rig
        {
            Db = db,
            Bus = bus,
            Twitch = twitch,
            Statuses = statuses,
            UpdatedHandler = new SuspiciousUserUpdatedHandler(statuses),
            MessageHandler = new SuspiciousUserMessageHandler(statuses, queue),
        };
    }

    private static SuspiciousUserUpdatedEvent Updated(string status) =>
        new()
        {
            BroadcasterId = Tenant,
            OccurredAt = DateTimeOffset.UtcNow,
            UserId = ViewerTwitchId,
            UserDisplayName = "Flagged",
            UserLogin = "flagged",
            ModeratorId = "42",
            ModeratorDisplayName = "Mod",
            LowTrustStatus = status,
        };

    private static SuspiciousUserMessageEvent Message(string status, string text = "hello there") =>
        new()
        {
            BroadcasterId = Tenant,
            OccurredAt = DateTimeOffset.UtcNow,
            UserId = ViewerTwitchId,
            UserDisplayName = "Flagged",
            UserLogin = "flagged",
            LowTrustStatus = status,
            MessageId = "msg-1",
            Text = text,
            BanEvasionEvaluation = "likely",
        };

    [Fact]
    public async Task An_update_stores_the_flag_and_a_later_none_clears_it()
    {
        Rig rig = await BuildAsync();

        await rig.UpdatedHandler.HandleAsync(Updated("restricted"));
        ChannelLowTrustStatus stored = await rig.Db.ChannelLowTrustStatuses.SingleAsync();
        stored.BroadcasterId.Should().Be(Tenant);
        stored.TwitchUserId.Should().Be(ViewerTwitchId);
        stored.Status.Should().Be("restricted");

        await rig.UpdatedHandler.HandleAsync(Updated("active_monitoring"));
        (await rig.Db.ChannelLowTrustStatuses.SingleAsync())
            .Status.Should()
            .Be("active_monitoring");

        await rig.UpdatedHandler.HandleAsync(Updated("none"));
        (await rig.Db.ChannelLowTrustStatuses.CountAsync()).Should().Be(0, "absence = none");
        (await rig.Statuses.GetAsync(Tenant, ViewerTwitchId)).Should().Be("none");
    }

    [Fact]
    public async Task A_flagged_message_stores_the_flag_with_the_ban_evasion_reading()
    {
        Rig rig = await BuildAsync();

        await rig.MessageHandler.HandleAsync(Message("restricted"));

        ChannelLowTrustStatus stored = await rig.Db.ChannelLowTrustStatuses.SingleAsync();
        stored.Status.Should().Be("restricted");
        stored.BanEvasionEvaluation.Should().Be("likely");
    }

    [Theory]
    [InlineData("restricted")]
    [InlineData("active_monitoring")]
    public async Task A_flagged_message_lands_in_the_moderation_queue_and_is_not_deleted(
        string status
    )
    {
        Rig rig = await BuildAsync();

        await rig.MessageHandler.HandleAsync(Message(status, "buy followers at spam dot example"));

        ModerationQueueItem item = await rig.Db.ModerationQueueItems.SingleAsync();
        item.Source.Should().Be(ModerationQueueSource.SuspiciousUser);
        item.Status.Should().Be(ModerationQueueStatus.Pending);
        item.BroadcasterId.Should().Be(Tenant);
        item.TargetTwitchUserId.Should().Be(ViewerTwitchId);
        item.AutoModMessageId.Should().Be("msg-1");
        item.MessageContentSnapshot.Should().Be("buy followers at spam dot example");
        item.AutoModCategory.Should().Be(status);
        rig.Twitch.ReceivedCalls().Should().BeEmpty("the message is never deleted or relayed");
    }

    [Fact]
    public async Task A_message_whose_flag_is_none_is_not_queued()
    {
        Rig rig = await BuildAsync();

        await rig.MessageHandler.HandleAsync(Message("none"));

        (await rig.Db.ModerationQueueItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_status_change_pushes_the_suspicious_users_domain_for_that_channel()
    {
        Rig rig = await BuildAsync();

        await rig.UpdatedHandler.HandleAsync(Updated("restricted"));

        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == Tenant
                    && e.Domain == "suspicious-users"
                    && e.EntityId == ViewerTwitchId
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unchanged_status_pushes_nothing()
    {
        Rig rig = await BuildAsync();
        await rig.UpdatedHandler.HandleAsync(Updated("restricted"));
        rig.Bus.ClearReceivedCalls();

        await rig.MessageHandler.HandleAsync(Message("restricted"));

        await rig
            .Bus.DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("restricted")]
    [InlineData("active_monitoring")]
    [InlineData("none")]
    public async Task The_viewer_card_context_carries_the_flag(string status)
    {
        Rig rig = await BuildAsync();
        await rig.UpdatedHandler.HandleAsync(Updated(status));
        ModerationService moderation = new(
            rig.Db,
            rig.Twitch,
            Substitute.For<ITwitchModeratorsApi>(),
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            rig.Bus
        );

        Result<UserModerationContextDto> context = await moderation.GetUserContextAsync(
            Tenant.ToString(),
            ViewerTwitchId,
            CancellationToken.None
        );

        context.Value.LowTrustStatus.Should().Be(status);
    }
}
