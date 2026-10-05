// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
using NomNomzBot.Application.Moderation.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the dashboard's automatic-shoutout switch reads and writes <c>Channel.AutoShoutoutEnabled</c>, the
/// column <c>AutoShoutoutScheduler</c> checks: a write survives a re-read from the store, the write touches
/// only the named channel, an unknown channel is refused, and the write sits behind the shoutout action gate.
/// </summary>
public sealed class ModerationControllerAutoShoutoutTests
{
    private static ModerationController Build(ApiTestDbContext db) =>
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
            Substitute.For<ITwitchChatApi>(),
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<ITemplateHelperValidator>(),
            Substitute.For<IModerationHistoryService>()
        );

    private static Guid SeedChannel(ApiTestDbContext db, string name, bool autoShoutout)
    {
        Guid id = Guid.CreateVersion7();
        db.Channels.Add(
            new()
            {
                Id = id,
                Name = name,
                NameNormalized = name,
                AutoShoutoutEnabled = autoShoutout,
            }
        );
        db.SaveChanges();
        return id;
    }

    private static async Task<bool> ReadAsync(ModerationController controller, Guid channelId)
    {
        IActionResult result = await controller.GetAutoShoutout(
            channelId.ToString(),
            CancellationToken.None
        );
        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok
            .Value.Should()
            .BeOfType<StatusResponseDto<ModerationController.AutoShoutoutDto>>()
            .Subject.Data!.Enabled;
    }

    [Fact]
    public async Task Turning_it_on_persists_and_a_re_read_from_the_store_shows_it_on()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db, "streamer", autoShoutout: false);
        ModerationController controller = Build(db);

        (await ReadAsync(controller, channelId)).Should().BeFalse();

        IActionResult put = await controller.SetAutoShoutout(
            channelId.ToString(),
            new(true),
            CancellationToken.None
        );

        put.Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == channelId))
            .AutoShoutoutEnabled.Should()
            .BeTrue();
        (await ReadAsync(controller, channelId)).Should().BeTrue();
    }

    [Fact]
    public async Task Turning_it_off_persists()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db, "streamer", autoShoutout: true);
        ModerationController controller = Build(db);

        await controller.SetAutoShoutout(channelId.ToString(), new(false), CancellationToken.None);

        db.ChangeTracker.Clear();
        (await ReadAsync(controller, channelId)).Should().BeFalse();
    }

    [Fact]
    public async Task A_write_for_one_channel_leaves_every_other_channel_unchanged()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid mine = SeedChannel(db, "mine", autoShoutout: false);
        Guid theirs = SeedChannel(db, "theirs", autoShoutout: false);
        ModerationController controller = Build(db);

        await controller.SetAutoShoutout(mine.ToString(), new(true), CancellationToken.None);

        db.ChangeTracker.Clear();
        (await ReadAsync(controller, mine)).Should().BeTrue();
        (await ReadAsync(controller, theirs)).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_channel_is_refused_and_nothing_is_created()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid existing = SeedChannel(db, "streamer", autoShoutout: false);
        ModerationController controller = Build(db);

        IActionResult put = await controller.SetAutoShoutout(
            Guid.CreateVersion7().ToString(),
            new(true),
            CancellationToken.None
        );

        put.Should().BeOfType<NotFoundObjectResult>();
        db.ChangeTracker.Clear();
        (await ReadAsync(controller, existing)).Should().BeFalse();
        (await db.Channels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void The_write_needs_the_shoutout_action_and_the_read_needs_moderation_read()
    {
        string? write = typeof(ModerationController)
            .GetMethod(nameof(ModerationController.SetAutoShoutout))!
            .GetCustomAttribute<RequireActionAttribute>()
            ?.ActionKey;
        string? read = typeof(ModerationController)
            .GetMethod(nameof(ModerationController.GetAutoShoutout))!
            .GetCustomAttribute<RequireActionAttribute>()
            ?.ActionKey;

        write.Should().Be("moderation:shoutout");
        read.Should().Be("moderation:read");
    }
}
