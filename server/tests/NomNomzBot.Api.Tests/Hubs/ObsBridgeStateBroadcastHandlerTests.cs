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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
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
            new()
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
            new()
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
            new()
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
