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
    public required int InstanceCount { get; init; }
    public required bool HasLeader { get; init; }
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
    public required bool Streaming { get; init; }
    public required bool Recording { get; init; }
}
