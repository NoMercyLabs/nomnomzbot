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

/// <summary>One item (source instance) placed in a scene — the per-scene visibility state
/// <see cref="ObsInputDto"/> cannot express, since a global input can appear in several scenes with a
/// different <c>sceneItemEnabled</c> in each.</summary>
public sealed record ObsSceneItemDto(int SceneItemId, string SourceName, bool Enabled);

/// <summary>Virtual camera output status (obs-websocket v5 <c>GetVirtualCamStatus</c>) — the same
/// single <c>outputActive</c> shape the stream/record/replay-buffer status requests answer with.</summary>
public sealed record ObsVirtualCamStatusDto(bool OutputActive);

/// <summary>OBS performance stats (obs-websocket v5 <c>GetStats</c>) — CPU/memory load and the
/// render-thread vs. output-thread frame counters used to detect dropped frames.</summary>
public sealed record ObsStatsDto(
    double CpuUsage,
    double MemoryUsage,
    double ActiveFps,
    int RenderTotalFrames,
    int RenderSkippedFrames,
    int OutputTotalFrames,
    int OutputSkippedFrames
);

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

/// <summary>REST body for the per-source visibility route: hide/show one item within one scene.</summary>
public sealed record ObsSourceVisibilityRequest(string SceneName, string SourceName, bool Visible);
