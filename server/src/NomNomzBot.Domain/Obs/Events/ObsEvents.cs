// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Obs.Events;

/// <summary>
/// An OBS event arrived from the channel's OBS instance (obs-control.md §2/§6) — via the direct
/// socket or forwarded by the leader bridge. Feeds the <c>obs_event</c> trigger surface.
/// </summary>
public sealed class ObsEventReceivedEvent : DomainEventBase
{
    /// <summary>The OBS-WS event type, e.g. <c>CurrentProgramSceneChanged</c>.</summary>
    public required string ObsEventType { get; init; }

    /// <summary>The raw OBS <c>eventData</c> JSON (may be empty for data-less events).</summary>
    public required string DataJson { get; init; }
}

/// <summary>The channel's bridge fleet changed (a bridge joined/left or the leader moved) — obs-control.md §2.</summary>
public sealed class ObsBridgeStateChangedEvent : DomainEventBase
{
    /// <summary>How many OBS bridge instances are connected.</summary>
    public required int InstanceCount { get; init; }

    /// <summary>True when at least one OBS instance is connected to lead.</summary>
    public required bool HasLeader { get; init; }

    /// <summary>The last error text. Empty when there is no error.</summary>
    public string? LastError { get; init; }
}

/// <summary>
/// The channel's direct OBS WebSocket connection was (re)established, carrying the REAL current
/// stream/record status read right then via <c>GetStreamStatus</c>/<c>GetRecordStatus</c>
/// (obs-control.md §2/§3.2/D1). OBS-WS never replays a <c>StreamStateChanged</c>/<c>RecordStateChanged</c>
/// event for a session that predates the connection, so without this a pre-existing live/recording
/// session stays invisible on the dashboard until the next actual start/stop transition.
/// </summary>
public sealed class ObsConnectionEstablishedEvent : DomainEventBase
{
    /// <summary>True when OBS is streaming at the time of the connection.</summary>
    public required bool Streaming { get; init; }

    /// <summary>True when OBS is recording at the time of the connection.</summary>
    public required bool Recording { get; init; }
}

/// <summary>
/// The channel's OBS streaming output started or stopped (obs-websocket v5
/// <c>StreamStateChanged</c>) — projected from <see cref="ObsEventReceivedEvent"/> by
/// <c>Infrastructure.Obs.EventHandlers.ObsAutomationStateForwarder</c> into the public
/// <c>obs.streaming.changed</c> automation event (S-STREAMDECK-OBS-REMAINDER), mirroring how
/// <c>SongChangedEvent</c> feeds <c>song.changed</c>.
/// </summary>
public sealed class ObsStreamingStateChangedEvent : DomainEventBase
{
    /// <summary>Whether the stream output is active right now.</summary>
    public required bool Active { get; init; }
}

/// <summary>
/// The channel's OBS recording output started, stopped, paused, or resumed (obs-websocket v5
/// <c>RecordStateChanged</c>) — projected the same way as <see cref="ObsStreamingStateChangedEvent"/>
/// into the public <c>obs.recording.changed</c> automation event.
/// </summary>
public sealed class ObsRecordingStateChangedEvent : DomainEventBase
{
    /// <summary>Whether the record output is active (started/resumed) right now.</summary>
    public required bool Active { get; init; }

    /// <summary>Whether an active recording is currently paused.</summary>
    public required bool Paused { get; init; }
}

/// <summary>
/// One OBS audio input's mute state changed (obs-websocket v5 <c>InputMuteStateChanged</c>) —
/// projected the same way into the public <c>obs.mute.changed</c> automation event.
/// </summary>
public sealed class ObsInputMuteStateChangedEvent : DomainEventBase
{
    /// <summary>The name of the OBS input.</summary>
    public required string InputName { get; init; }

    /// <summary>True when the input is now muted.</summary>
    public required bool Muted { get; init; }
}
