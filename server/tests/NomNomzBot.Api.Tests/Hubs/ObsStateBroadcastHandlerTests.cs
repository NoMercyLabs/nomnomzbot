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
/// Proves a live OBS change reaches the channel's dashboards as one <c>ObsStateChanged</c> message that
/// carries the new value, so the OBS page flips without a refetch.
/// </summary>
public sealed class ObsStateBroadcastHandlerTests
{
    [Fact]
    public async Task A_program_scene_change_reaches_the_dashboard_with_the_scene_name()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.ProgramScene,
                SceneName = "Just Chatting",
            }
        );

        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Kind == "programScene"
                    && dto.SceneName == "Just Chatting"
                    && dto.Active == null
                    && dto.Timestamp.Length > 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_scene_list_change_carries_every_scene_in_order()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.SceneList,
                SceneNames = ["Starting", "Game", "Ending"],
            }
        );

        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.Kind == "sceneList"
                    && dto.SceneNames != null
                    && dto.SceneNames.SequenceEqual(new[] { "Starting", "Game", "Ending" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        ObsStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                Kind = ObsStateKinds.ProgramScene,
                SceneName = "Game",
            }
        );

        await notifier
            .DidNotReceive()
            .SendObsStateAsync(
                Arg.Any<string>(),
                Arg.Any<ObsStateChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
