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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NomNomzBot.Infrastructure.Obs.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves S-STREAMDECK-OBS-REMAINDER's live-state forward end to end: <see cref="ObsAutomationStateForwarder"/>
/// turns the generic <see cref="ObsEventReceivedEvent"/> (the same signal <c>ObsEventTriggerSource</c>
/// already dispatches the <c>obs_event</c> pipeline trigger from) into the typed
/// <see cref="ObsStreamingStateChangedEvent"/>/<see cref="ObsRecordingStateChangedEvent"/>/
/// <see cref="ObsInputMuteStateChangedEvent"/> domain events that feed the public
/// <c>obs.streaming.changed</c>/<c>obs.recording.changed</c>/<c>obs.mute.changed</c> automation events —
/// mirroring <c>SongChangedProjectorTests</c>' proof shape for <c>song.changed</c>.
/// </summary>
public sealed class ObsAutomationStateForwarderTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ab001");

    [Fact]
    public async Task StreamStateChanged_publishes_the_typed_streaming_event()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "StreamStateChanged",
                DataJson = """{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}""",
            }
        );

        ObsStreamingStateChangedEvent published = bus
            .Published.OfType<ObsStreamingStateChangedEvent>()
            .Single();
        published.BroadcasterId.Should().Be(ChannelId);
        published.Active.Should().BeTrue();

        // The descriptor's projection is a pure passthrough of the already-projected event.
        ObsStreamingStateAutomationEventDescriptor descriptor = new();
        AutomationObsStreamingStateDto viaDescriptor = (AutomationObsStreamingStateDto)
            descriptor.ProjectPayload(published);
        viaDescriptor.Active.Should().BeTrue();
    }

    [Fact]
    public async Task StreamStateChanged_stopped_publishes_inactive()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "StreamStateChanged",
                DataJson =
                    """{"outputActive":false,"outputState":"OBS_WEBSOCKET_OUTPUT_STOPPED"}""",
            }
        );

        bus.Published.OfType<ObsStreamingStateChangedEvent>().Single().Active.Should().BeFalse();
    }

    [Fact]
    public async Task RecordStateChanged_paused_sets_both_active_and_paused()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "RecordStateChanged",
                DataJson = """{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_PAUSED"}""",
            }
        );

        ObsRecordingStateChangedEvent published = bus
            .Published.OfType<ObsRecordingStateChangedEvent>()
            .Single();
        published.Active.Should().BeTrue();
        published.Paused.Should().BeTrue();

        ObsRecordingStateAutomationEventDescriptor descriptor = new();
        AutomationObsRecordingStateDto viaDescriptor = (AutomationObsRecordingStateDto)
            descriptor.ProjectPayload(published);
        viaDescriptor.Active.Should().BeTrue();
        viaDescriptor.Paused.Should().BeTrue();
    }

    [Fact]
    public async Task RecordStateChanged_started_is_not_paused()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "RecordStateChanged",
                DataJson = """{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}""",
            }
        );

        ObsRecordingStateChangedEvent published = bus
            .Published.OfType<ObsRecordingStateChangedEvent>()
            .Single();
        published.Active.Should().BeTrue();
        published.Paused.Should().BeFalse();
    }

    [Fact]
    public async Task InputMuteStateChanged_publishes_the_typed_mute_event()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "InputMuteStateChanged",
                DataJson = """{"inputName":"Mic/Aux","inputMuted":true}""",
            }
        );

        ObsInputMuteStateChangedEvent published = bus
            .Published.OfType<ObsInputMuteStateChangedEvent>()
            .Single();
        published.InputName.Should().Be("Mic/Aux");
        published.Muted.Should().BeTrue();

        ObsMuteStateAutomationEventDescriptor descriptor = new();
        AutomationObsMuteStateDto viaDescriptor = (AutomationObsMuteStateDto)
            descriptor.ProjectPayload(published);
        viaDescriptor.InputName.Should().Be("Mic/Aux");
        viaDescriptor.Muted.Should().BeTrue();
    }

    [Fact]
    public async Task InputMuteStateChanged_without_an_input_name_publishes_nothing()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "InputMuteStateChanged",
                DataJson = """{"inputMuted":true}""",
            }
        );

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Unrelated_obs_event_types_publish_nothing()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "CurrentProgramSceneChanged",
                DataJson = """{"sceneName":"Scene 1"}""",
            }
        );

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_the_platform_sentinel_broadcaster()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = Guid.Empty,
                ObsEventType = "StreamStateChanged",
                DataJson = """{"outputActive":true}""",
            }
        );

        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_data_json_is_ignored_not_thrown()
    {
        RecordingEventBus bus = new();
        ObsAutomationStateForwarder sut = new(
            bus,
            NullLogger<ObsAutomationStateForwarder>.Instance
        );

        await sut.HandleAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelId,
                ObsEventType = "StreamStateChanged",
                DataJson = "not json",
            }
        );

        bus.Published.Should().BeEmpty();
    }
}
