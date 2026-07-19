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
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs;

/// <summary>
/// Typed OBS ops (obs-control.md §3.1/D7) — each builds the exact OBS-WS request and rides
/// <see cref="IObsTransport"/> with a fresh command id. The D7 nuances live here: source visibility
/// resolves the numeric scene-item id first; volume takes dB XOR multiplier; media verbs map to the
/// <c>OBS_WEBSOCKET_MEDIA_INPUT_ACTION_*</c> constants; the browser refresh presses the input's
/// <c>refreshnocache</c> button.
/// </summary>
public class ObsControlService : IObsControlService
{
    private readonly IObsTransport _transport;

    public ObsControlService(IObsTransport transport)
    {
        _transport = transport;
    }

    // ── Scenes / items ──────────────────────────────────────────────────────

    public Task<Result> SwitchSceneAsync(
        Guid broadcasterId,
        string sceneName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "SetCurrentProgramScene",
            new Dictionary<string, object?> { ["sceneName"] = sceneName },
            ct
        );

    public Task<Result> SetPreviewSceneAsync(
        Guid broadcasterId,
        string sceneName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "SetCurrentPreviewScene",
            new Dictionary<string, object?> { ["sceneName"] = sceneName },
            ct
        );

    public async Task<Result> SetSourceVisibleAsync(
        Guid broadcasterId,
        string sceneName,
        string sourceName,
        bool visible,
        CancellationToken ct = default
    )
    {
        // D7: SetSceneItemEnabled wants the NUMERIC item id — resolve it from the source name first.
        Result<ObsResponse> item = await _transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequest(
                "GetSceneItemId",
                new Dictionary<string, object?>
                {
                    ["sceneName"] = sceneName,
                    ["sourceName"] = sourceName,
                }
            ),
            ct
        );
        Result itemStatus = ToStatus(item);
        if (itemStatus.IsFailure)
            return itemStatus;
        if (
            item.Value.ResponseData is null
            || !item.Value.ResponseData.TryGetValue("sceneItemId", out object? idValue)
            || idValue is null
        )
            return Result.Failure(
                $"Source '{sourceName}' was not found in scene '{sceneName}'.",
                "NOT_FOUND"
            );

        return await SendStatusAsync(
            broadcasterId,
            "SetSceneItemEnabled",
            new Dictionary<string, object?>
            {
                ["sceneName"] = sceneName,
                ["sceneItemId"] = (int)Convert.ToDouble(idValue),
                ["sceneItemEnabled"] = visible,
            },
            ct
        );
    }

    // ── Audio / inputs ──────────────────────────────────────────────────────

    public Task<Result> SetInputMuteAsync(
        Guid broadcasterId,
        string inputName,
        bool muted,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "SetInputMute",
            new Dictionary<string, object?> { ["inputName"] = inputName, ["inputMuted"] = muted },
            ct
        );

    public Task<Result> ToggleInputMuteAsync(
        Guid broadcasterId,
        string inputName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "ToggleInputMute",
            new Dictionary<string, object?> { ["inputName"] = inputName },
            ct
        );

    public Task<Result> SetInputVolumeAsync(
        Guid broadcasterId,
        string inputName,
        double? volumeDb,
        double? volumeMul,
        CancellationToken ct = default
    )
    {
        // D7: exactly one of dB / multiplier.
        if (volumeDb is null == volumeMul is null)
            return Task.FromResult(
                Errors.ValidationFailed("Provide exactly one of volume_db or volume_mul.")
            );
        Dictionary<string, object?> data = new() { ["inputName"] = inputName };
        if (volumeDb is double db)
            data["inputVolumeDb"] = db;
        else
            data["inputVolumeMul"] = volumeMul;
        return SendStatusAsync(broadcasterId, "SetInputVolume", data, ct);
    }

    // ── Filters ─────────────────────────────────────────────────────────────

    public Task<Result> SetFilterEnabledAsync(
        Guid broadcasterId,
        string sourceName,
        string filterName,
        bool enabled,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "SetSourceFilterEnabled",
            new Dictionary<string, object?>
            {
                ["sourceName"] = sourceName,
                ["filterName"] = filterName,
                ["filterEnabled"] = enabled,
            },
            ct
        );

    // ── Outputs ─────────────────────────────────────────────────────────────

    public Task<Result> SetRecordingAsync(
        Guid broadcasterId,
        RecordAction action,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            action switch
            {
                RecordAction.Start => "StartRecord",
                RecordAction.Stop => "StopRecord",
                RecordAction.Toggle => "ToggleRecord",
                RecordAction.Pause => "PauseRecord",
                RecordAction.Resume => "ResumeRecord",
                RecordAction.Split => "SplitRecordFile",
                _ => "ToggleRecord",
            },
            null,
            ct
        );

    public Task<Result> SetStreamingAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    ) => SendStatusAsync(broadcasterId, ToggleRequest(action, "Stream"), null, ct);

    public Task<Result> SetReplayBufferAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    ) => SendStatusAsync(broadcasterId, ToggleRequest(action, "ReplayBuffer"), null, ct);

    public Task<Result> SaveReplayBufferAsync(Guid broadcasterId, CancellationToken ct = default) =>
        SendStatusAsync(broadcasterId, "SaveReplayBuffer", null, ct);

    public Task<Result> SetVirtualCamAsync(
        Guid broadcasterId,
        ObsToggle action,
        CancellationToken ct = default
    ) => SendStatusAsync(broadcasterId, ToggleRequest(action, "VirtualCam"), null, ct);

    private static string ToggleRequest(ObsToggle action, string output) =>
        action switch
        {
            ObsToggle.Start => $"Start{output}",
            ObsToggle.Stop => $"Stop{output}",
            _ => $"Toggle{output}",
        };

    // ── Transitions / media / hotkeys ───────────────────────────────────────

    public Task<Result> SetCurrentTransitionAsync(
        Guid broadcasterId,
        string transitionName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "SetCurrentSceneTransition",
            new Dictionary<string, object?> { ["transitionName"] = transitionName },
            ct
        );

    public async Task<Result> TriggerStudioTransitionAsync(
        Guid broadcasterId,
        int? durationMs,
        CancellationToken ct = default
    )
    {
        if (durationMs is int duration)
        {
            Result setDuration = await SendStatusAsync(
                broadcasterId,
                "SetCurrentSceneTransitionDuration",
                new Dictionary<string, object?> { ["transitionDuration"] = duration },
                ct
            );
            if (setDuration.IsFailure)
                return setDuration;
        }
        return await SendStatusAsync(broadcasterId, "TriggerStudioModeTransition", null, ct);
    }

    public Task<Result> TriggerMediaAsync(
        Guid broadcasterId,
        string inputName,
        MediaAction action,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "TriggerMediaInputAction",
            new Dictionary<string, object?>
            {
                ["inputName"] = inputName,
                ["mediaAction"] = action switch
                {
                    MediaAction.Play => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY",
                    MediaAction.Pause => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PAUSE",
                    MediaAction.Stop => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP",
                    MediaAction.Restart => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART",
                    MediaAction.Next => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_NEXT",
                    _ => "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PREVIOUS",
                },
            },
            ct
        );

    public Task<Result> TriggerHotkeyAsync(
        Guid broadcasterId,
        string hotkeyName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "TriggerHotkeyByName",
            new Dictionary<string, object?> { ["hotkeyName"] = hotkeyName },
            ct
        );

    public Task<Result> RefreshBrowserAsync(
        Guid broadcasterId,
        string inputName,
        CancellationToken ct = default
    ) =>
        SendStatusAsync(
            broadcasterId,
            "PressInputPropertiesButton",
            new Dictionary<string, object?>
            {
                ["inputName"] = inputName,
                ["propertyName"] = "refreshnocache",
            },
            ct
        );

    public async Task<Result<string>> ScreenshotAsync(
        Guid broadcasterId,
        string sourceName,
        string imageFormat,
        CancellationToken ct = default
    )
    {
        Result<ObsResponse> response = await _transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequest(
                "GetSourceScreenshot",
                new Dictionary<string, object?>
                {
                    ["sourceName"] = sourceName,
                    ["imageFormat"] = imageFormat,
                }
            ),
            ct
        );
        Result status = ToStatus(response);
        if (status.IsFailure)
            return Result.Failure<string>(status.ErrorMessage!, status.ErrorCode!);
        string? image = response.Value.ResponseData?.GetValueOrDefault("imageData") as string;
        return image is null
            ? Result.Failure<string>("OBS returned no image data.", "OBS_ERROR")
            : Result.Success(image);
    }

    // ── Generic pass-through ────────────────────────────────────────────────

    public Task<Result<ObsResponse>> RequestAsync(
        Guid broadcasterId,
        ObsRequest request,
        CancellationToken ct = default
    ) => _transport.SendAsync(broadcasterId, Guid.CreateVersion7(), request, ct);

    public Task<Result<IReadOnlyList<ObsResponse>>> RequestBatchAsync(
        Guid broadcasterId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    ) => _transport.SendBatchAsync(broadcasterId, Guid.CreateVersion7(), batch, ct);

    public Task<Result<ObsResponse>> CallVendorAsync(
        Guid broadcasterId,
        string vendorName,
        string requestType,
        IReadOnlyDictionary<string, object?>? data,
        CancellationToken ct = default
    ) =>
        _transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequest(
                "CallVendorRequest",
                new Dictionary<string, object?>
                {
                    ["vendorName"] = vendorName,
                    ["requestType"] = requestType,
                    ["requestData"] = data,
                }
            ),
            ct
        );

    // ── State ───────────────────────────────────────────────────────────────

    public async Task<Result<ObsStateDto>> GetStateAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsResponse>> batch = await _transport.SendBatchAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequestBatch([
                new ObsRequest("GetCurrentProgramScene", null),
                new ObsRequest("GetStreamStatus", null),
                new ObsRequest("GetRecordStatus", null),
                new ObsRequest("GetReplayBufferStatus", null),
            ]),
            ct
        );
        if (batch.IsFailure)
            return Result.Failure<ObsStateDto>(batch.ErrorMessage!, batch.ErrorCode!);

        IReadOnlyList<ObsResponse> results = batch.Value;
        string? scene =
            results
                .ElementAtOrDefault(0)
                ?.ResponseData?.GetValueOrDefault("currentProgramSceneName") as string;
        bool streaming = GetBool(results.ElementAtOrDefault(1), "outputActive");
        bool recording = GetBool(results.ElementAtOrDefault(2), "outputActive");
        bool recordPaused = GetBool(results.ElementAtOrDefault(2), "outputPaused");
        string? timecode =
            results.ElementAtOrDefault(2)?.ResponseData?.GetValueOrDefault("outputTimecode")
            as string;
        // GetReplayBufferStatus fails when the buffer output doesn't exist — that reads as inactive.
        bool replayActive = GetBool(results.ElementAtOrDefault(3), "outputActive");

        return Result.Success(
            new ObsStateDto(scene, streaming, recording, recordPaused, replayActive, timecode)
        );
    }

    public async Task<Result<IReadOnlyList<ObsSceneDto>>> GetScenesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<ObsResponse> response = await _transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequest("GetSceneList", null),
            ct
        );
        Result status = ToStatus(response);
        if (status.IsFailure)
            return Result.Failure<IReadOnlyList<ObsSceneDto>>(
                status.ErrorMessage!,
                status.ErrorCode!
            );

        string? current =
            response.Value.ResponseData?.GetValueOrDefault("currentProgramSceneName") as string;
        List<ObsSceneDto> scenes = [];
        if (response.Value.ResponseData?.GetValueOrDefault("scenes") is string scenesJson)
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(
                scenesJson
            );
            foreach (System.Text.Json.JsonElement item in doc.RootElement.EnumerateArray())
                if (item.TryGetProperty("sceneName", out System.Text.Json.JsonElement nameEl))
                {
                    string name = nameEl.GetString() ?? "";
                    scenes.Add(new ObsSceneDto(name, name == current));
                }
        }
        return Result.Success<IReadOnlyList<ObsSceneDto>>(scenes);
    }

    public async Task<Result<IReadOnlyList<ObsInputDto>>> GetInputsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<ObsResponse> response = await _transport.SendAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequest("GetInputList", null),
            ct
        );
        Result status = ToStatus(response);
        if (status.IsFailure)
            return Result.Failure<IReadOnlyList<ObsInputDto>>(
                status.ErrorMessage!,
                status.ErrorCode!
            );

        List<(string Name, string Kind)> raw = [];
        if (response.Value.ResponseData?.GetValueOrDefault("inputs") is string inputsJson)
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(
                inputsJson
            );
            foreach (System.Text.Json.JsonElement item in doc.RootElement.EnumerateArray())
            {
                string name = item.TryGetProperty(
                    "inputName",
                    out System.Text.Json.JsonElement nameEl
                )
                    ? nameEl.GetString() ?? ""
                    : "";
                string kind = item.TryGetProperty(
                    "inputKind",
                    out System.Text.Json.JsonElement kindEl
                )
                    ? kindEl.GetString() ?? ""
                    : "";
                raw.Add((name, kind));
            }
        }

        if (raw.Count == 0)
            return Result.Success<IReadOnlyList<ObsInputDto>>([]);

        // Enrich each input with its live mute + volume so the dashboard mixer opens on real levels — one batch of
        // GetInputMute/GetInputVolume per input, HaltOnFailure off so a non-audio input (browser/image) that
        // rejects these simply leaves mute/volume null (that is how the mixer tells audio inputs apart).
        List<ObsRequest> probes = [];
        foreach ((string Name, string Kind) input in raw)
        {
            probes.Add(
                new ObsRequest(
                    "GetInputMute",
                    new Dictionary<string, object?> { ["inputName"] = input.Name }
                )
            );
            probes.Add(
                new ObsRequest(
                    "GetInputVolume",
                    new Dictionary<string, object?> { ["inputName"] = input.Name }
                )
            );
        }

        Result<IReadOnlyList<ObsResponse>> probeBatch = await _transport.SendBatchAsync(
            broadcasterId,
            Guid.CreateVersion7(),
            new ObsRequestBatch(probes, HaltOnFailure: false),
            ct
        );
        IReadOnlyList<ObsResponse>? probeResults = probeBatch.IsSuccess ? probeBatch.Value : null;

        List<ObsInputDto> inputs = [];
        for (int i = 0; i < raw.Count; i++)
        {
            bool? muted = null;
            double? volumeDb = null;
            if (probeResults is not null && probeResults.Count > (2 * i) + 1)
            {
                ObsResponse muteResp = probeResults[2 * i];
                if (muteResp.Ok && muteResp.ResponseData?.GetValueOrDefault("inputMuted") is bool m)
                    muted = m;

                ObsResponse volResp = probeResults[(2 * i) + 1];
                if (
                    volResp.Ok
                    && volResp.ResponseData?.GetValueOrDefault("inputVolumeDb") is double db
                )
                    volumeDb = db;
            }
            inputs.Add(new ObsInputDto(raw[i].Name, raw[i].Kind, muted, volumeDb));
        }
        return Result.Success<IReadOnlyList<ObsInputDto>>(inputs);
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    private async Task<Result> SendStatusAsync(
        Guid broadcasterId,
        string requestType,
        Dictionary<string, object?>? data,
        CancellationToken ct
    ) =>
        ToStatus(
            await _transport.SendAsync(
                broadcasterId,
                Guid.CreateVersion7(),
                new ObsRequest(requestType, data),
                ct
            )
        );

    private static Result ToStatus(Result<ObsResponse> response)
    {
        if (response.IsFailure)
            return Result.Failure(response.ErrorMessage!, response.ErrorCode!);
        return response.Value.Ok
            ? Result.Success()
            : Result.Failure(response.Value.Error ?? "OBS rejected the request.", "OBS_ERROR");
    }

    private static bool GetBool(ObsResponse? response, string key) =>
        response?.ResponseData?.GetValueOrDefault(key) is bool value && value;
}
