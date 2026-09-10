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
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ObsConnectionEstablishedBroadcastHandler"/> forwards the REAL current
/// stream/record status — read the moment the OBS WebSocket connection (re)establishes — to the
/// channel's dashboards immediately, so a session already live/recording when the bot (re)connects
/// shows up without waiting for a future start/stop event; a platform-level event never reaches the hub.
/// </summary>
public sealed class ObsConnectionEstablishedBroadcastHandlerTests
{
    [Fact]
    public async Task A_connect_while_OBS_is_already_live_and_recording_forwards_both_flags_true()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsConnectionEstablishedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Streaming = true,
                Recording = true,
            }
        );

        await notifier
            .Received(1)
            .SendObsLiveStateAsync(
                channel.ToString(),
                Arg.Is<ObsLiveStateDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Streaming
                    && dto.Recording
                    && dto.Timestamp.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_connect_while_OBS_is_idle_forwards_both_flags_false()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsConnectionEstablishedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Streaming = false,
                Recording = false,
            }
        );

        await notifier
            .Received(1)
            .SendObsLiveStateAsync(
                channel.ToString(),
                Arg.Is<ObsLiveStateDto>(dto => !dto.Streaming && !dto.Recording),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ObsConnectionEstablishedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Streaming = true,
                Recording = true,
            }
        );

        await notifier
            .DidNotReceive()
            .SendObsLiveStateAsync(
                Arg.Any<string>(),
                Arg.Any<ObsLiveStateDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
