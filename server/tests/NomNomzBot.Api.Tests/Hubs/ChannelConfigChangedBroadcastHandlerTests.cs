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
using NomNomzBot.Domain.Platform.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ChannelConfigChangedBroadcastHandler"/> (E5) is the single generic forwarder for every
/// config-CRUD domain: the hub push carries the event's domain/entity/action fields untouched, and a
/// platform-level event (no channel) never reaches the hub.
/// </summary>
public sealed class ChannelConfigChangedBroadcastHandlerTests
{
    [Fact]
    public async Task Forwards_domain_entity_and_action_to_the_channel_group()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ChannelConfigChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = channel,
                Domain = "commands",
                EntityId = "cmd-1",
                Action = "created",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Domain == "commands"
                    && dto.EntityId == "cmd-1"
                    && dto.Action == "created"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_domain_wide_change_carries_a_null_entity_id()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ChannelConfigChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = channel,
                Domain = "tts-config",
                EntityId = null,
                Action = "updated",
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto => dto.EntityId == null && dto.Domain == "tts-config"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ChannelConfigChangedBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = Guid.Empty,
                Domain = "features",
                Action = "toggled",
            }
        );

        await notifier
            .DidNotReceive()
            .SendConfigChangedAsync(
                Arg.Any<string>(),
                Arg.Any<ConfigChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
