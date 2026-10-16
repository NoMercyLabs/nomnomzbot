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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Infrastructure.Obs.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves each raw OBS-WS event that changes the OBS page becomes one typed
/// <see cref="ObsDashboardStateChangedEvent"/> with the new value, and that other events are left alone.
/// </summary>
public sealed class ObsDashboardStateForwarderTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();

    private static async Task<ObsDashboardStateChangedEvent?> Forward(string type, string data)
    {
        RecordingEventBus bus = new();
        ObsDashboardStateForwarder sut = new(bus, NullLogger<ObsDashboardStateForwarder>.Instance);

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = type,
                DataJson = data,
            }
        );

        return bus.Published.OfType<ObsDashboardStateChangedEvent>().SingleOrDefault();
    }

    [Fact]
    public async Task A_program_scene_change_publishes_the_new_scene_name()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "CurrentProgramSceneChanged",
            """{"sceneName":"Just Chatting","sceneUuid":"a"}"""
        );

        published.Should().NotBeNull();
        published!.BroadcasterId.Should().Be(ChannelId);
        published.Kind.Should().Be(ObsStateKinds.ProgramScene);
        published.SceneName.Should().Be("Just Chatting");
    }

    [Fact]
    public async Task A_preview_scene_change_publishes_the_preview_scene()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "CurrentPreviewSceneChanged",
            """{"sceneName":"Game"}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.PreviewScene);
        published.SceneName.Should().Be("Game");
    }

    [Theory]
    [InlineData("StreamStateChanged", "stream")]
    [InlineData("ReplayBufferStateChanged", "replayBuffer")]
    [InlineData("VirtualcamStateChanged", "virtualCam")]
    public async Task An_output_state_change_publishes_its_active_flag(string type, string kind)
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            type,
            """{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}"""
        );

        published!.Kind.Should().Be(kind);
        published.Active.Should().BeTrue();
    }

    [Fact]
    public async Task A_paused_recording_publishes_active_and_paused()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "RecordStateChanged",
            """{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_PAUSED"}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.Record);
        published.Active.Should().BeTrue();
        published.Paused.Should().BeTrue();
    }

    [Fact]
    public async Task Studio_mode_publishes_its_enabled_flag_as_active()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "StudioModeStateChanged",
            """{"studioModeEnabled":true}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.StudioMode);
        published.Active.Should().BeTrue();
    }

    [Fact]
    public async Task An_input_mute_and_volume_change_carry_the_input_and_value()
    {
        ObsDashboardStateChangedEvent? mute = await Forward(
            "InputMuteStateChanged",
            """{"inputName":"Mic","inputMuted":true}"""
        );
        ObsDashboardStateChangedEvent? volume = await Forward(
            "InputVolumeChanged",
            """{"inputName":"Mic","inputVolumeMul":0.5,"inputVolumeDb":-6.02}"""
        );

        mute!.Kind.Should().Be(ObsStateKinds.InputMute);
        mute.InputName.Should().Be("Mic");
        mute.Muted.Should().BeTrue();
        volume!.Kind.Should().Be(ObsStateKinds.InputVolume);
        volume.InputName.Should().Be("Mic");
        volume.VolumeDb.Should().BeApproximately(-6.02, 0.001);
    }

    [Fact]
    public async Task A_scene_list_change_publishes_the_names_in_the_order_the_scenes_read_returns()
    {
        // GET obs/scenes keeps the OBS array order, so the live list matches the loaded list.
        ObsDashboardStateChangedEvent? published = await Forward(
            "SceneListChanged",
            """{"scenes":[{"sceneName":"Ending","sceneIndex":0},{"sceneName":"Game","sceneIndex":1},{"sceneName":"Starting","sceneIndex":2}]}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.SceneList);
        published.SceneNames.Should().Equal("Ending", "Game", "Starting");
    }

    [Theory]
    [InlineData(
        "CurrentSceneCollectionChanged",
        """{"sceneCollectionName":"Gaming"}""",
        "sceneCollection",
        "Gaming"
    )]
    [InlineData(
        "CurrentProfileChanged",
        """{"profileName":"Low bitrate"}""",
        "profile",
        "Low bitrate"
    )]
    public async Task A_collection_or_profile_switch_publishes_the_new_name(
        string type,
        string data,
        string kind,
        string name
    )
    {
        ObsDashboardStateChangedEvent? published = await Forward(type, data);

        published!.Kind.Should().Be(kind);
        published.Name.Should().Be(name);
    }

    [Theory]
    [InlineData(
        "SceneCollectionListChanged",
        """{"sceneCollections":["Chatting","Gaming"]}""",
        "sceneCollectionList"
    )]
    [InlineData("ProfileListChanged", """{"profiles":["Chatting","Gaming"]}""", "profileList")]
    public async Task A_collection_or_profile_list_change_publishes_every_name_in_order(
        string type,
        string data,
        string kind
    )
    {
        ObsDashboardStateChangedEvent? published = await Forward(type, data);

        published!.Kind.Should().Be(kind);
        published.Names.Should().Equal("Chatting", "Gaming");
    }

    [Fact]
    public async Task A_collection_or_profile_switch_without_a_name_publishes_nothing()
    {
        (await Forward("CurrentSceneCollectionChanged", "{}")).Should().BeNull();
        (await Forward("CurrentProfileChanged", "{}")).Should().BeNull();
    }

    [Fact]
    public async Task A_scene_item_visibility_change_carries_scene_item_and_flag()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "SceneItemEnableStateChanged",
            """{"sceneName":"Game","sceneItemId":7,"sceneItemEnabled":false}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.SceneItemVisibility);
        published.SceneName.Should().Be("Game");
        published.SceneItemId.Should().Be(7);
        published.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_scene_item_lock_change_carries_scene_item_and_flag()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "SceneItemLockStateChanged",
            """{"sceneName":"Game","sceneItemId":7,"sceneItemLocked":true}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.SceneItemLock);
        published.SceneName.Should().Be("Game");
        published.SceneItemId.Should().Be(7);
        published.Locked.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_filter_toggle_carries_source_filter_and_flag()
    {
        ObsDashboardStateChangedEvent? published = await Forward(
            "SourceFilterEnableStateChanged",
            """{"sourceName":"Mic","filterName":"Noise Gate","filterEnabled":true}"""
        );

        published!.Kind.Should().Be(ObsStateKinds.SourceFilter);
        published.SourceName.Should().Be("Mic");
        published.FilterName.Should().Be("Noise Gate");
        published.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_filter_toggle_without_a_source_or_filter_name_publishes_nothing()
    {
        (
            await Forward(
                "SourceFilterEnableStateChanged",
                """{"filterName":"x","filterEnabled":true}"""
            )
        )
            .Should()
            .BeNull();
        (
            await Forward(
                "SourceFilterEnableStateChanged",
                """{"sourceName":"x","filterEnabled":true}"""
            )
        )
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task An_unrelated_or_unparsable_event_publishes_nothing()
    {
        (await Forward("InputCreated", """{"inputName":"Mic"}""")).Should().BeNull();
        (await Forward("CurrentProgramSceneChanged", "not json")).Should().BeNull();
        (await Forward("CurrentProgramSceneChanged", "{}")).Should().BeNull();
    }
}
