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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves a saved or removed shoutout message (the channel template or a per-target override) tells every open
/// dashboard of that channel: one <see cref="ChannelConfigChangedEvent"/> in the <c>shoutouts</c> domain, scoped
/// to the channel, and none when nothing changed.
/// </summary>
public sealed class ModerationControllerShoutoutLiveSyncTests
{
    private static ModerationController Build(ApiTestDbContext db, IEventBus eventBus)
    {
        ITemplateHelperValidator validator = Substitute.For<ITemplateHelperValidator>();
        validator
            .Validate(Arg.Any<string?>(), Arg.Any<TemplateHelperContext>())
            .Returns(Result.Success());

        return new(
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
            validator,
            Substitute.For<IModerationHistoryService>(),
            Substitute.For<IMassBanConsentService>(),
            [],
            Substitute.For<IShoutoutSuggestionService>(),
            eventBus
        );
    }

    private static Guid SeedChannel(ApiTestDbContext db)
    {
        Guid id = Guid.CreateVersion7();
        db.Channels.Add(
            new()
            {
                Id = id,
                Name = "streamer",
                NameNormalized = "streamer",
            }
        );
        db.SaveChanges();
        return id;
    }

    private static ChannelConfigChangedEvent Published(IEventBus eventBus)
    {
        IEnumerable<ICall> calls = eventBus.ReceivedCalls();
        return calls
            .Select(c => c.GetArguments()[0])
            .OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
    }

    [Fact]
    public async Task Saving_the_channel_template_publishes_a_shoutouts_event_for_that_channel()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db);
        IEventBus eventBus = Substitute.For<IEventBus>();
        ModerationController controller = Build(db, eventBus);

        IActionResult result = await controller.SetShoutoutTemplate(
            channelId.ToString(),
            new("Go follow {{user}}"),
            CancellationToken.None
        );

        result.Should().BeOfType<NoContentResult>();
        ChannelConfigChangedEvent published = Published(eventBus);
        published.BroadcasterId.Should().Be(channelId);
        published.Domain.Should().Be("shoutouts");
        published.Action.Should().Be("updated");
    }

    [Fact]
    public async Task Saving_an_override_publishes_created_then_updated_for_that_channel()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db);
        IEventBus eventBus = Substitute.For<IEventBus>();
        ModerationController controller = Build(db, eventBus);

        await controller.SetShoutoutOverride(
            channelId.ToString(),
            new("1001", "Target", "first line"),
            CancellationToken.None
        );
        await controller.SetShoutoutOverride(
            channelId.ToString(),
            new("1001", "Target", "second line"),
            CancellationToken.None
        );

        List<ChannelConfigChangedEvent> events = eventBus
            .ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<ChannelConfigChangedEvent>()
            .ToList();
        events.Select(e => e.Action).Should().Equal("created", "updated");
        events
            .Should()
            .AllSatisfy(e =>
            {
                e.BroadcasterId.Should().Be(channelId);
                e.Domain.Should().Be("shoutouts");
                e.EntityId.Should().Be("1001");
            });
    }

    [Fact]
    public async Task Removing_an_override_publishes_deleted_and_a_missing_one_publishes_nothing()
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channelId = SeedChannel(db);
        IEventBus eventBus = Substitute.For<IEventBus>();
        ModerationController controller = Build(db, eventBus);
        await controller.SetShoutoutOverride(
            channelId.ToString(),
            new("1001", "Target", "a line"),
            CancellationToken.None
        );
        eventBus.ClearReceivedCalls();

        await controller.DeleteShoutoutOverride(
            channelId.ToString(),
            "1001",
            CancellationToken.None
        );
        ChannelConfigChangedEvent published = Published(eventBus);
        published.BroadcasterId.Should().Be(channelId);
        published.Domain.Should().Be("shoutouts");
        published.Action.Should().Be("deleted");
        published.EntityId.Should().Be("1001");

        eventBus.ClearReceivedCalls();
        IActionResult missing = await controller.DeleteShoutoutOverride(
            channelId.ToString(),
            "1001",
            CancellationToken.None
        );

        missing.Should().BeOfType<NotFoundObjectResult>();
        eventBus.ReceivedCalls().Should().BeEmpty();
    }
}
