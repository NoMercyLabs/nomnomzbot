// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Stream.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="AdBreakBeganBroadcastHandler"/> forwards a starting ad break to dashboard clients, including
/// the manual-break requester fields when present — this event had no handler of any kind before this slice.
/// </summary>
public sealed class AdBreakBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_ManualBreak_MapsRequesterAndDuration_AsAdBreakBeginChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AdBreakBeganBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset startedAt = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new AdBreakBeganEvent
            {
                BroadcasterId = channel,
                DurationSeconds = 180,
                IsAutomatic = false,
                StartedAt = startedAt,
                RequesterUserId = "mod-1",
                RequesterDisplayName = "ModName",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "ad_break_begin",
                Arg.Is<object>(data =>
                    data is AdBreakBeganAlertDto
                    && ((AdBreakBeganAlertDto)data).DurationSeconds == 180
                    && ((AdBreakBeganAlertDto)data).IsAutomatic == false
                    && ((AdBreakBeganAlertDto)data).StartedAt == startedAt
                    && ((AdBreakBeganAlertDto)data).RequesterUserId == "mod-1"
                    && ((AdBreakBeganAlertDto)data).RequesterDisplayName == "ModName"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_AutomaticBreak_MapsNullRequester()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AdBreakBeganBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new AdBreakBeganEvent
            {
                BroadcasterId = channel,
                DurationSeconds = 90,
                IsAutomatic = true,
                StartedAt = DateTimeOffset.UtcNow,
                RequesterUserId = null,
                RequesterDisplayName = null,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "ad_break_begin",
                Arg.Is<object>(data =>
                    data is AdBreakBeganAlertDto
                    && ((AdBreakBeganAlertDto)data).IsAutomatic == true
                    && ((AdBreakBeganAlertDto)data).RequesterUserId == null
                    && ((AdBreakBeganAlertDto)data).RequesterDisplayName == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        AdBreakBeganBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new AdBreakBeganEvent
            {
                BroadcasterId = Guid.Empty,
                DurationSeconds = 60,
                IsAutomatic = true,
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
    }
}
