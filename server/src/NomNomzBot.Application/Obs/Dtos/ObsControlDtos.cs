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

/// <summary>One scene transition OBS knows about (obs-websocket v5 <c>GetSceneTransitionList</c>) —
/// mirrors <see cref="ObsSceneDto"/>'s shape: name + whether it is the one currently selected.</summary>
public sealed record ObsTransitionDto(string Name, bool IsCurrent);

/// <summary>One filter attached to a source (obs-websocket v5 <c>GetSourceFilterList</c>) — name,
/// kind, whether it's enabled, and its position in the source's filter chain. <c>filterSettings</c>
/// is provider-specific free-form data the dashboard's filter list has no use for, so it's left out.</summary>
public sealed record ObsFilterDto(string Name, string Kind, bool Enabled, int Index);

/// <summary>Studio mode status (obs-websocket v5 <c>GetStudioModeEnabled</c>) — the same single-flag
/// shape as <see cref="ObsVirtualCamStatusDto"/>.</summary>
public sealed record ObsStudioModeStatusDto(bool Enabled);

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

/// <summary>REST body for the studio-mode route: turn studio mode on or off.</summary>
public sealed record ObsStudioModeRequest(bool Enabled);

/// <summary>REST body for the studio-mode transition route: cut the preview scene to program.
/// <paramref name="DurationMs"/> is null to use the transition's own configured duration.</summary>
public sealed record ObsStudioTransitionRequest(int? DurationMs);

/// <summary>REST body for the scene-transition-select route: make one transition the active one.</summary>
public sealed record ObsCurrentTransitionRequest(string TransitionName);

/// <summary>REST body for the filter-enable route: enable/disable one filter on one source.</summary>
public sealed record ObsFilterEnabledRequest(string SourceName, string FilterName, bool Enabled);

/// <summary>REST body for the media-input-control route (play/pause/restart/stop/next/previous).</summary>
public sealed record ObsMediaActionRequest(string InputName, MediaAction Action);

/// <summary>REST body for the browser-source-refresh route.</summary>
public sealed record ObsRefreshBrowserRequest(string InputName);

/// <summary>REST body for the hotkey-trigger route.</summary>
public sealed record ObsHotkeyTriggerRequest(string HotkeyName);

/// <summary>REST body for the source-screenshot route (obs-websocket v5 <c>GetSourceScreenshot</c>).
/// <paramref name="ImageFormat"/> is an encoder OBS supports (e.g. <c>png</c>, <c>jpg</c>).</summary>
public sealed record ObsScreenshotRequest(string SourceName, string ImageFormat);

/// <summary>REST body for the vendor pass-through route (obs-websocket v5 <c>CallVendorRequest</c>) —
/// the escape hatch for third-party OBS plugins that add their own vendor requests.</summary>
public sealed record ObsVendorRequest(
    string VendorName,
    string RequestType,
    IReadOnlyDictionary<string, object?>? RequestData
);
