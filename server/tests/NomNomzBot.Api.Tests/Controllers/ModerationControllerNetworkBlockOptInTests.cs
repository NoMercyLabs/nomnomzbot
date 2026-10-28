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

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the network-block switch reads and writes <c>Channel.AcceptsNetworkBlocks</c>, the column the
/// network block checks before it acts in a channel: it is off for a new channel, a write survives a re-read
/// from the store, touches only the named channel, an unknown channel is refused, and the write is owner-gated.
/// </summary>
public sealed class ModerationControllerNetworkBlockOptInTests
{
    private static ModerationController Build(ApiTestDbContext db, IEventBus? eventBus = null) =>
        new(
            Substitute.For<IModerationService>(),
            Substitute.For<IOperatorNetworkBanService>(),
            Substitute.For<IViewerReportService>(),
            Substitute.For<IModerationQueueService>(),
            Substitute.For<ISharedBanService>(),
            Substitute.For<INetworkNukeService>(),
            Substitute.For<IModerationEscalationService>(),
            Substitute.For<ICurrentUserService>(),
            db,
            TimeProvider.System,
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<ITemplateHelperValidator>(),
            Substitute.For<IModerationHistoryService>(),
            Substitute.For<IMassBanConsentService>(),
            [],
            Substitute.For<IShoutoutSuggestionService>(),
            eventBus ?? Substitute.For<IEventBus>()
        );

    private static Guid SeedChannel(ApiTestDbContext db, string name)
    {
        Guid id = Guid.CreateVersion7();
        db.Channels.Add(
            new()
            {
                Id = id,
                Name = name,
                NameNormalized = name,
            }
        );
        db.SaveChanges();
        return id;
    }

    private static async Task<bool> ReadAsync(ModerationController controller, Guid channelId)
    {
        IActionResult result = await controller.GetNetworkBlockOptIn(
            channelId.ToString(),
            CancellationToken.None
        );
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok
            .Value.Should()
            .BeOfType<StatusResponseDto<NetworkBlockOptInDto>>()
            .Subject.Data!.Accepts;
    }

    [Fact]
    public async Task A_new_channel_has_network_blocks_off()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db, "streamer");

        (await ReadAsync(Build(db), channelId)).Should().BeFalse();
    }

    [Fact]
    public async Task Turning_it_on_persists_and_turning_it_off_again_persists()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db, "streamer");
        ModerationController controller = Build(db);

        IActionResult on = await controller.SetNetworkBlockOptIn(
            channelId.ToString(),
            new(true),
            CancellationToken.None
        );

        on.Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == channelId))
            .AcceptsNetworkBlocks.Should()
            .BeTrue();
        (await ReadAsync(controller, channelId)).Should().BeTrue();

        await controller.SetNetworkBlockOptIn(
            channelId.ToString(),
            new(false),
            CancellationToken.None
        );
        db.ChangeTracker.Clear();
        (await ReadAsync(controller, channelId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_write_for_one_channel_leaves_every_other_channel_unchanged()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid mine = SeedChannel(db, "mine");
        Guid theirs = SeedChannel(db, "theirs");
        ModerationController controller = Build(db);

        await controller.SetNetworkBlockOptIn(mine.ToString(), new(true), CancellationToken.None);

        db.ChangeTracker.Clear();
        (await ReadAsync(controller, mine)).Should().BeTrue();
        (await ReadAsync(controller, theirs)).Should().BeFalse();
    }

    [Fact]
    public async Task Changing_the_switch_tells_the_channels_open_dashboards_to_refetch_it()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db, "streamer");
        IEventBus eventBus = Substitute.For<IEventBus>();
        ModerationController controller = Build(db, eventBus);

        await controller.SetNetworkBlockOptIn(
            channelId.ToString(),
            new(true),
            CancellationToken.None
        );

        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == channelId
                    && e.Domain == "network-blocks"
                    && e.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_refused_write_publishes_nothing()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        IEventBus eventBus = Substitute.For<IEventBus>();

        await Build(db, eventBus)
            .SetNetworkBlockOptIn(
                Guid.CreateVersion7().ToString(),
                new(true),
                CancellationToken.None
            );

        await eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_channel_is_refused_on_read_and_write_and_nothing_is_created()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        SeedChannel(db, "streamer");
        ModerationController controller = Build(db);
        string unknown = Guid.CreateVersion7().ToString();

        (await controller.SetNetworkBlockOptIn(unknown, new(true), CancellationToken.None))
            .Should()
            .BeOfType<NotFoundObjectResult>();
        (await controller.GetNetworkBlockOptIn(unknown, CancellationToken.None))
            .Should()
            .BeOfType<NotFoundObjectResult>();
        (await db.Channels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void The_write_needs_the_owner_action_and_the_read_needs_moderation_read()
    {
        string? write = typeof(ModerationController)
            .GetMethod(nameof(ModerationController.SetNetworkBlockOptIn))!
            .GetCustomAttribute<RequireActionAttribute>()
            ?.ActionKey;
        string? read = typeof(ModerationController)
            .GetMethod(nameof(ModerationController.GetNetworkBlockOptIn))!
            .GetCustomAttribute<RequireActionAttribute>()
            ?.ActionKey;

        write.Should().Be("moderation:networkblock:optin");
        read.Should().Be("moderation:read");
    }
}
