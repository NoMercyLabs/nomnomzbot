// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;

namespace NomNomzBot.Application.Obs.Services;

/// <summary>
/// Typed OBS control (obs-control.md §3.1). Each op builds the exact OBS-WS request (D7 nuances —
/// e.g. source visibility resolves the scene-item id first; volume is dB XOR multiplier) and
/// dispatches through <see cref="IObsTransport"/> with a fresh command id. Everything fallible is a
/// <see cref="Result"/> with a stable code — no connection, no leader, or an OBS error never throws.
/// </summary>
public interface IObsControlService
{
    // ── Scenes / items ──
    Task<Result> SwitchSceneAsync(
        Guid broadcasterId,
        string sceneName,
        CancellationToken ct = default
    );
    Task<Result> SetPreviewSceneAsync(
        Guid broadcasterId,
        string sceneName,
        CancellationToken ct = default
    );
    Task<Result> SetSourceVisibleAsync(
        Guid broadcasterId,
        string sceneName,
        string sourceName,
        bool visible,
        CancellationToken ct = default
    );

    // ── Audio / inputs ──
    Task<Result> SetInputMuteAsync(
        Guid broadcasterId,
        string inputName,
        bool muted,
        CancellationToken ct = default
    );
    Task<Result> ToggleInputMuteAsync(
        Guid broadcasterId,
        string inputName,
        CancellationToken ct = default
    );
    Task<Result> SetInputVolumeAsync(
        Guid broadcasterId,
        string inputName,
        double? volumeDb,
        double? volumeMul,
        CancellationToken ct = default
    );

    // ── Filters ──
    Task<Result> SetFilterEnabledAsync(
        Guid broadcasterId,
        string sourceName,
        string filterName,
        bool enabled,
        CancellationToken ct = default
    );

    // ── Outputs ──
    Task<Result> SetRecordingAsync(
        Guid broadcasterId,
        RecordAction action,
        CancellationToken ct = default
    );
    Task<Result> SetStreamingAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    );
    Task<Result> SetReplayBufferAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    );
    Task<Result> SaveReplayBufferAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> SetVirtualCamAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    );

    // ── Transitions / media / hotkeys ──
    Task<Result> SetCurrentTransitionAsync(
        Guid broadcasterId,
        string transitionName,
        CancellationToken ct = default
    );
    Task<Result> TriggerStudioTransitionAsync(
        Guid broadcasterId,
        int? durationMs,
        CancellationToken ct = default
    );
    Task<Result> TriggerMediaAsync(
        Guid broadcasterId,
        string inputName,
        MediaAction action,
        CancellationToken ct = default
    );
    Task<Result> TriggerHotkeyAsync(
        Guid broadcasterId,
        string hotkeyName,
        CancellationToken ct = default
    );
    Task<Result> RefreshBrowserAsync(
        Guid broadcasterId,
        string inputName,
        CancellationToken ct = default
    );
    Task<Result<string>> ScreenshotAsync(
        Guid broadcasterId,
        string sourceName,
        string imageFormat,
        CancellationToken ct = default
    );

    // ── Generic pass-through (full surface) ──
    Task<Result<ObsResponse>> RequestAsync(
        Guid broadcasterId,
        ObsRequest request,
        CancellationToken ct = default
    );
    Task<Result<IReadOnlyList<ObsResponse>>> RequestBatchAsync(
        Guid broadcasterId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    );
    Task<Result<ObsResponse>> CallVendorAsync(
        Guid broadcasterId,
        string vendorName,
        string requestType,
        IReadOnlyDictionary<string, object?>? data,
        CancellationToken ct = default
    );

    // ── State (dashboard + {{obs.*}} vars) ──
    Task<Result<ObsStateDto>> GetStateAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsSceneDto>>> GetScenesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
    Task<Result<IReadOnlyList<ObsInputDto>>> GetInputsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
