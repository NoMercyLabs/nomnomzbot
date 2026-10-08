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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the heat threshold actually enforces (S-OWN23) and — more importantly — proves what it will
/// never do. Before this handler existed the threshold was decorative: the crossing event had no
/// consumer at all, so a streamer could set "auto-timeout at 80" and be protected by nothing.
///
/// <para>The immunity tests here are the ones that matter. An automated punishment that can reach the
/// broadcaster or a moderator is worse than no automation, so those assertions must fail loudly if
/// anyone ever reorders the guard behind the action.</para>
/// </summary>
public sealed class HeatThresholdAutoTimeoutHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000c1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192c000-0000-7000-8000-0000000000c2");
    private static readonly Guid ViewerUserId = Guid.Parse("0192c000-0000-7000-8000-0000000000c3");
    private const string BroadcasterTwitchId = "700001";
    private const string ViewerTwitchId = "900042";

    private static async Task<(
        HeatThresholdAutoTimeoutHandler Handler,
        IModerationService Moderation,
        ModerationServiceTestDbContext Db,
        RecordingEventBus Bus
    )> BuildWithStateAsync(
        bool autoTimeoutOn,
        int timeoutSeconds = 600,
        bool subjectIsModerator = false,
        bool timeoutFails = false
    )
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = BroadcasterTwitchId,
                OwnerUserId = OwnerUserId,
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.Users.Add(
            new()
            {
                Id = ViewerUserId,
                TwitchUserId = ViewerTwitchId,
                Username = "heatedviewer",
                UsernameNormalized = "heatedviewer",
                DisplayName = "HeatedViewer",
            }
        );
        if (subjectIsModerator)
            db.ChannelModerators.Add(
                new()
                {
                    ChannelId = Channel,
                    UserId = ViewerUserId,
                    Role = "moderator",
                }
            );
        await db.SaveChangesAsync();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .GetAutomodConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new AutomodConfigDto(
                        new(false, []),
                        new(false, 0),
                        new(false, []),
                        new(false, 0),
                        HeatTimeoutThreshold: 80,
                        AutoTimeoutOnHeat: autoTimeoutOn,
                        HeatTimeoutSeconds: timeoutSeconds
                    )
                )
            );
        moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                timeoutFails
                    ? Result.Failure<ModerationActionResult>(
                        "Twitch refused: missing scope moderator:manage:banned_users",
                        "PLATFORM_ERROR"
                    )
                    : Result.Success(new ModerationActionResult(true, null))
            );

        ModerationQueueService queue = new(
            db,
            Substitute.For<IUserService>(),
            Substitute.For<ITwitchModerationApi>(),
            moderation,
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(),
            TimeProvider.System,
            NullLogger<ModerationQueueService>.Instance
        );
        RecordingEventBus bus = new();
        HeatThresholdAutoTimeoutHandler handler = new(
            db,
            moderation,
            queue,
            bus,
            NullLogger<HeatThresholdAutoTimeoutHandler>.Instance
        );
        return (handler, moderation, db, bus);
    }

    private static async Task<(
        HeatThresholdAutoTimeoutHandler Handler,
        IModerationService Moderation
    )> BuildAsync(bool autoTimeoutOn, int timeoutSeconds = 600, bool subjectIsModerator = false)
    {
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation, _, _) =
            await BuildWithStateAsync(autoTimeoutOn, timeoutSeconds, subjectIsModerator);
        return (handler, moderation);
    }

    private static UserHeatThresholdCrossedEvent Crossing(string twitchUserId, Guid userId) =>
        new()
        {
            BroadcasterId = Channel,
            SubjectUserId = userId,
            SubjectTwitchUserId = twitchUserId,
            HeatScore = 85m,
            Threshold = 80,
        };

    [Fact]
    public async Task WhenEnabled_ACrossingTimesTheViewerOut_ForTheConfiguredLength()
    {
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation) = await BuildAsync(
            autoTimeoutOn: true,
            timeoutSeconds: 900
        );

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        await moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                OwnerUserId,
                ViewerTwitchId,
                900,
                Arg.Is<string?>(reason => reason != null && reason.Contains("heat")),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task WhenDisabled_ACrossingActionsNobody()
    {
        // The default. Heat still accrues and still flags; it simply never punishes on its own.
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation) = await BuildAsync(
            autoTimeoutOn: false
        );

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        await moderation
            .DidNotReceiveWithAnyArgs()
            .TimeoutAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task TheBroadcasterIsNeverAutoTimedOut_EvenWithEnforcementOn()
    {
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation) = await BuildAsync(
            autoTimeoutOn: true
        );

        await handler.HandleAsync(Crossing(BroadcasterTwitchId, OwnerUserId));

        await moderation
            .DidNotReceiveWithAnyArgs()
            .TimeoutAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task AModeratorIsNeverAutoTimedOut_EvenWithEnforcementOn()
    {
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation) = await BuildAsync(
            autoTimeoutOn: true,
            subjectIsModerator: true
        );

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        await moderation
            .DidNotReceiveWithAnyArgs()
            .TimeoutAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task WhenDisabled_ACrossingLandsInTheModerationQueueAsOneHeatFlag_AndNothingIsActioned()
    {
        (
            HeatThresholdAutoTimeoutHandler handler,
            IModerationService moderation,
            ModerationServiceTestDbContext db,
            RecordingEventBus bus
        ) = await BuildWithStateAsync(autoTimeoutOn: false);

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        ModerationQueueItem row = await db.ModerationQueueItems.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Source.Should().Be(ModerationQueueSource.HeatThreshold);
        row.Status.Should().Be(ModerationQueueStatus.Pending);
        row.TargetUserId.Should().Be(ViewerUserId);
        row.TargetTwitchUserId.Should().Be(ViewerTwitchId);
        row.TargetUsernameSnapshot.Should().Be("heatedviewer");
        row.AutoModMessageId.Should().BeNull("a heat flag holds no chat message");
        row.MessageContentSnapshot.Should().Contain("85").And.Contain("80");
        await moderation
            .DidNotReceiveWithAnyArgs()
            .TimeoutAsync(default!, default, default!, default);
        bus.Published.OfType<UserHeatAutoTimeoutFailedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task WhenDisabled_ASecondCrossingEventForTheSameViewerWhileStillHot_AddsNoSecondRow()
    {
        // The projection fires the event only on the upward crossing, so a second HandleAsync for the same
        // still-pending subject is a repeat delivery (or a re-fire while hot): it must not stack a row.
        (HeatThresholdAutoTimeoutHandler handler, _, ModerationServiceTestDbContext db, _) =
            await BuildWithStateAsync(autoTimeoutOn: false);

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));
        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        (await db.ModerationQueueItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task WhenDisabled_ANewCrossingAfterTheOldFlagWasResolved_FlagsAgain()
    {
        (HeatThresholdAutoTimeoutHandler handler, _, ModerationServiceTestDbContext db, _) =
            await BuildWithStateAsync(autoTimeoutOn: false);
        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));
        ModerationQueueItem first = await db.ModerationQueueItems.SingleAsync();
        first.Status = ModerationQueueStatus.Approved;
        await db.SaveChangesAsync();

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        List<ModerationQueueItem> rows = await db.ModerationQueueItems.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Count(r => r.Status == ModerationQueueStatus.Pending).Should().Be(1);
    }

    [Fact]
    public async Task WhenEnabled_AndTheTimeoutSucceeds_NothingIsQueuedAndNoFailureIsReported()
    {
        (
            HeatThresholdAutoTimeoutHandler handler,
            _,
            ModerationServiceTestDbContext db,
            RecordingEventBus bus
        ) = await BuildWithStateAsync(autoTimeoutOn: true);

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        (await db.ModerationQueueItems.CountAsync()).Should().Be(0);
        bus.Published.OfType<UserHeatAutoTimeoutFailedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task WhenEnabled_AndThePlatformRefusesTheTimeout_TheFailureIsPublishedForTheInbox()
    {
        (
            HeatThresholdAutoTimeoutHandler handler,
            _,
            ModerationServiceTestDbContext db,
            RecordingEventBus bus
        ) = await BuildWithStateAsync(autoTimeoutOn: true, timeoutFails: true);

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        UserHeatAutoTimeoutFailedEvent failed = bus
            .Published.OfType<UserHeatAutoTimeoutFailedEvent>()
            .Single();
        failed.BroadcasterId.Should().Be(Channel);
        failed.SubjectUserId.Should().Be(ViewerUserId);
        failed.SubjectTwitchUserId.Should().Be(ViewerTwitchId);
        failed.SubjectUsername.Should().Be("heatedviewer");
        failed.Error.Should().Contain("moderator:manage:banned_users");
        failed.HeatScore.Should().Be(85m);
        failed.Threshold.Should().Be(80);
        (await db.ModerationQueueItems.CountAsync())
            .Should()
            .Be(0, "a failed timeout is reported to the inbox, not stacked into the review queue");
    }

    [Fact]
    public async Task AModeratorWhoCrossesTheThreshold_IsFlaggedForAHuman_NotActioned()
    {
        // The handler's own log has always said "flagged, not actioned" for an immune subject.
        (
            HeatThresholdAutoTimeoutHandler handler,
            IModerationService moderation,
            ModerationServiceTestDbContext db,
            _
        ) = await BuildWithStateAsync(autoTimeoutOn: true, subjectIsModerator: true);

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        (await db.ModerationQueueItems.SingleAsync())
            .Source.Should()
            .Be(ModerationQueueSource.HeatThreshold);
        await moderation
            .DidNotReceiveWithAnyArgs()
            .TimeoutAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task AZeroTimeoutLength_FallsBackToTenMinutes_RatherThanASilentNoOp()
    {
        // A config stored before HeatTimeoutSeconds existed deserializes to 0. A 0-second timeout is
        // not a timeout, so the handler must substitute the documented default instead of issuing one.
        (HeatThresholdAutoTimeoutHandler handler, IModerationService moderation) = await BuildAsync(
            autoTimeoutOn: true,
            timeoutSeconds: 0
        );

        await handler.HandleAsync(Crossing(ViewerTwitchId, ViewerUserId));

        await moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                ViewerTwitchId,
                600,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
