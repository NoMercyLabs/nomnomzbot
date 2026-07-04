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
using NomNomzBot.Domain.Moderation.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the Shield Mode broadcasters forward activation/deactivation to dashboard clients — these events had
/// no handler of any kind before this slice.
/// </summary>
public sealed class ShieldModeBroadcastHandlersTests
{
    [Fact]
    public async Task ShieldModeBegan_MapsModeratorAndStartedAt_AsShieldModeBeginChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ShieldModeBeganBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset startedAt = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new ShieldModeBeganEvent
            {
                BroadcasterId = channel,
                ModeratorId = "mod-1",
                ModeratorDisplayName = "ModName",
                StartedAt = startedAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "shield_mode_begin",
                Arg.Is<object>(data =>
                    data is ShieldModeBeganAlertDto
                    && ((ShieldModeBeganAlertDto)data).ModeratorId == "mod-1"
                    && ((ShieldModeBeganAlertDto)data).ModeratorDisplayName == "ModName"
                    && ((ShieldModeBeganAlertDto)data).StartedAt == startedAt
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ShieldModeEnded_MapsModeratorAndEndedAt_AsShieldModeEndChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ShieldModeEndedBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset endedAt = new(2026, 7, 1, 12, 30, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new ShieldModeEndedEvent
            {
                BroadcasterId = channel,
                ModeratorId = "mod-1",
                ModeratorDisplayName = "ModName",
                EndedAt = endedAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "shield_mode_end",
                Arg.Is<object>(data =>
                    data is ShieldModeEndedAlertDto
                    && ((ShieldModeEndedAlertDto)data).ModeratorId == "mod-1"
                    && ((ShieldModeEndedAlertDto)data).EndedAt == endedAt
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ShieldModeBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ShieldModeBeganBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ShieldModeBeganEvent
            {
                BroadcasterId = Guid.Empty,
                ModeratorId = "mod-1",
                ModeratorDisplayName = "x",
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
