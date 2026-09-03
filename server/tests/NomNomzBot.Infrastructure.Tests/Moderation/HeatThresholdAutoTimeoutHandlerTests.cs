// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
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
        IModerationService Moderation
    )> BuildAsync(bool autoTimeoutOn, int timeoutSeconds = 600, bool subjectIsModerator = false)
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
            .Returns(Result.Success(new ModerationActionResult(true, null)));

        HeatThresholdAutoTimeoutHandler handler = new(
            db,
            moderation,
            NullLogger<HeatThresholdAutoTimeoutHandler>.Instance
        );
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
            .TimeoutAsync(default!, default, default!, default, default, default, default);
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
            .TimeoutAsync(default!, default, default!, default, default, default, default);
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
            .TimeoutAsync(default!, default, default!, default, default, default, default);
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
