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
using NomNomzBot.Domain.Obs.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ObsBridgeStateBroadcastHandler"/> forwards a bridge fleet change to the channel's
/// dashboards with the instance count and leader flag intact — the push that lets the OBS page's bridge
/// indicator go live — and that a platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class ObsBridgeStateBroadcastHandlerTests
{
    [Fact]
    public async Task Forwards_instance_count_and_leader_flag_to_the_channel()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsBridgeStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = channel,
                InstanceCount = 2,
                HasLeader = true,
            }
        );

        await notifier
            .Received(1)
            .SendObsBridgeStateAsync(
                channel.ToString(),
                Arg.Is<ObsBridgeStateDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.InstanceCount == 2
                    && dto.HasLeader
                    && dto.Timestamp.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_bridge_going_fully_offline_forwards_zero_instances_and_no_leader()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsBridgeStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = channel,
                InstanceCount = 0,
                HasLeader = false,
            }
        );

        await notifier
            .Received(1)
            .SendObsBridgeStateAsync(
                channel.ToString(),
                Arg.Is<ObsBridgeStateDto>(dto => dto.InstanceCount == 0 && !dto.HasLeader),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ObsBridgeStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ObsBridgeStateChangedEvent
            {
                BroadcasterId = Guid.Empty,
                InstanceCount = 1,
                HasLeader = true,
            }
        );

        await notifier
            .DidNotReceive()
            .SendObsBridgeStateAsync(
                Arg.Any<string>(),
                Arg.Any<ObsBridgeStateDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
