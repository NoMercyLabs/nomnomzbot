// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Obs.Dtos;

/// <summary>Start/stop/toggle for OBS outputs (streaming, replay buffer, virtual cam).</summary>
public enum ObsToggle
{
    Start,
    Stop,
    Toggle,
}

/// <summary>Recording control verbs (obs-control.md §3.1).</summary>
public enum RecordAction
{
    Start,
    Stop,
    Toggle,
    Pause,
    Resume,
    Split,
}

/// <summary>Media-input verbs → <c>OBS_WEBSOCKET_MEDIA_INPUT_ACTION_*</c>.</summary>
public enum MediaAction
{
    Play,
    Pause,
    Stop,
    Restart,
    Next,
    Previous,
}

/// <summary>OBS-WS RequestBatch execution types (wire values match obs-websocket v5).</summary>
public enum ObsBatchExecution
{
    SerialRealtime = 0,
    SerialFrame = 1,
    Parallel = 2,
}

/// <summary>One raw OBS-WS request (the generic pass-through surface).</summary>
public sealed record ObsRequest(
    string RequestType,
    IReadOnlyDictionary<string, object?>? RequestData
);

/// <summary>A raw OBS-WS request batch (op 8).</summary>
public sealed record ObsRequestBatch(
    IReadOnlyList<ObsRequest> Requests,
    ObsBatchExecution Execution = ObsBatchExecution.SerialRealtime,
    bool HaltOnFailure = false
);

/// <summary>The outcome of one OBS-WS request as the caller sees it.</summary>
public sealed record ObsResponse(
    bool Ok,
    IReadOnlyDictionary<string, object?>? ResponseData,
    string? Error
);

/// <summary>Live OBS state for the dashboard and the <c>{{obs.*}}</c> template vars.</summary>
public sealed record ObsStateDto(
    string? CurrentScene,
    bool Streaming,
    bool Recording,
    bool RecordPaused,
    bool ReplayBufferActive,
    string? RecordTimecode
);

public sealed record ObsSceneDto(string Name, bool IsCurrent);

public sealed record ObsInputDto(string Name, string Kind, bool? Muted, double? VolumeDb);

/// <summary>REST body for the scene-switch route.</summary>
public sealed record ObsSceneRequest(string Scene);

/// <summary>REST body for start/stop/toggle output routes.</summary>
public sealed record ObsToggleRequest(ObsToggle Action);

/// <summary>REST body for the recording route.</summary>
public sealed record ObsRecordRequest(RecordAction Action);

/// <summary>REST body for the audio-mixer mute route: set an input's mute to an absolute state.</summary>
public sealed record ObsInputMuteRequest(string InputName, bool Muted);

/// <summary>REST body for the audio-mixer volume route: set an input's volume in decibels (OBS's dB scale).</summary>
public sealed record ObsInputVolumeRequest(string InputName, double VolumeDb);
