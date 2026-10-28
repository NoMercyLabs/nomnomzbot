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
    public async Task A_collection_switch_carries_its_name_and_a_profile_list_carries_its_names()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.SceneCollection,
                Name = "Gaming",
            }
        );
        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.ProfileList,
                Names = ["Untitled", "Low bitrate"],
            }
        );

        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.Kind == "sceneCollection" && dto.Name == "Gaming"
                ),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.Kind == "profileList"
                    && dto.Names != null
                    && dto.Names.SequenceEqual(new[] { "Untitled", "Low bitrate" })
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
    public async Task A_filter_toggle_and_a_lock_change_carry_their_new_values()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        ObsStateBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.SourceFilter,
                SourceName = "Mic",
                FilterName = "Noise Gate",
                Enabled = true,
            }
        );
        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                Kind = ObsStateKinds.SceneItemLock,
                SceneName = "Game",
                SceneItemId = 7,
                Locked = true,
            }
        );

        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.Kind == "sourceFilter"
                    && dto.SourceName == "Mic"
                    && dto.FilterName == "Noise Gate"
                    && dto.Enabled == true
                ),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .Received(1)
            .SendObsStateAsync(
                channel.ToString(),
                Arg.Is<ObsStateChangedDto>(dto =>
                    dto.Kind == "sceneItemLock"
                    && dto.SceneName == "Game"
                    && dto.SceneItemId == 7
                    && dto.Locked == true
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
