// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.PipelineActions;

// Output-control + raw pass-through actions (obs-control.md §5, Broadcaster floor in the palette).

/// <summary>Recording control. Config: <c>action: start|stop|toggle|pause|resume|split</c>.</summary>
public sealed class ObsRecordingAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_recording";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string verb = Param(ctx, action, "action")?.Trim().ToLowerInvariant() ?? "toggle";
        RecordAction record = verb switch
        {
            "start" => RecordAction.Start,
            "stop" => RecordAction.Stop,
            "pause" => RecordAction.Pause,
            "resume" => RecordAction.Resume,
            "split" => RecordAction.Split,
            _ => RecordAction.Toggle,
        };
        return Map(
            ctx,
            await Obs.SetRecordingAsync(ctx.BroadcasterId, record, ctx.CancellationToken),
            $"recording {verb}"
        );
    }
}

/// <summary>Streaming control. Config: <c>action: start|stop|toggle</c>.</summary>
public sealed class ObsStreamingAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_streaming";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string verb = Param(ctx, action, "action")?.Trim().ToLowerInvariant() ?? "toggle";
        return Map(
            ctx,
            await Obs.SetStreamingAsync(
                ctx.BroadcasterId,
                ParseToggle(verb),
                ctx.CancellationToken
            ),
            $"streaming {verb}"
        );
    }
}

/// <summary>Replay-buffer output control. Config: <c>action: start|stop|toggle</c>.</summary>
public sealed class ObsReplayBufferAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_replay_buffer";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string verb = Param(ctx, action, "action")?.Trim().ToLowerInvariant() ?? "toggle";
        return Map(
            ctx,
            await Obs.SetReplayBufferAsync(
                ctx.BroadcasterId,
                ParseToggle(verb),
                ctx.CancellationToken
            ),
            $"replay buffer {verb}"
        );
    }
}

/// <summary>Virtual-cam control. Config: <c>action: start|stop|toggle</c>.</summary>
public sealed class ObsVirtualCamAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_virtual_cam";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string verb = Param(ctx, action, "action")?.Trim().ToLowerInvariant() ?? "toggle";
        return Map(
            ctx,
            await Obs.SetVirtualCamAsync(
                ctx.BroadcasterId,
                ParseToggle(verb),
                ctx.CancellationToken
            ),
            $"virtual cam {verb}"
        );
    }
}

/// <summary>Raw OBS-WS request. Config: <c>request_type</c>, <c>request_data</c> (JSON object).</summary>
public sealed class ObsRequestAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_request";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "request_type", out string type, out ActionResult failure))
            return failure;
        Result<ObsResponse> response = await Obs.RequestAsync(
            ctx.BroadcasterId,
            new ObsRequest(type, ReadDataObject(action, "request_data")),
            ctx.CancellationToken
        );
        if (response.IsFailure)
        {
            ctx.Variables["obs.last_error"] = response.ErrorMessage ?? "OBS request failed";
            return ActionResult.Failure(response.ErrorMessage ?? "OBS request failed");
        }
        if (!response.Value.Ok)
        {
            ctx.Variables["obs.last_error"] = response.Value.Error ?? "OBS rejected the request";
            return ActionResult.Failure(response.Value.Error ?? "OBS rejected the request");
        }
        ctx.Variables["obs.response"] = JsonSerializer.Serialize(
            response.Value.ResponseData ?? new Dictionary<string, object?>()
        );
        return ActionResult.Success($"'{type}' ok");
    }

    internal static Dictionary<string, object?>? ReadDataObject(ActionDefinition action, string key)
    {
        if (action.Parameters is null || !action.Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        if (elem.ValueKind != JsonValueKind.Object)
            return null;
        Dictionary<string, object?> data = new();
        foreach (JsonProperty property in elem.EnumerateObject())
            data[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        return data;
    }
}

/// <summary>Raw OBS-WS request batch. Config: <c>requests</c> (array), <c>execution?</c>, <c>halt_on_failure?</c>.</summary>
public sealed class ObsRequestBatchAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_request_batch";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (
            action.Parameters is null
            || !action.Parameters.TryGetValue("requests", out JsonElement requestsEl)
            || requestsEl.ValueKind != JsonValueKind.Array
        )
            return ActionResult.Failure("obs_request_batch needs a 'requests' array");

        List<ObsRequest> requests = [];
        foreach (JsonElement item in requestsEl.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("request_type", out JsonElement typeEl)
            )
                return ActionResult.Failure("each batch entry needs a 'request_type'");
            Dictionary<string, object?>? data = null;
            if (
                item.TryGetProperty("request_data", out JsonElement dataEl)
                && dataEl.ValueKind == JsonValueKind.Object
            )
            {
                data = new Dictionary<string, object?>();
                foreach (JsonProperty property in dataEl.EnumerateObject())
                    data[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText(),
                    };
            }
            requests.Add(new ObsRequest(typeEl.GetString() ?? "", data));
        }

        string execution = Param(ctx, action, "execution")?.Trim().ToLowerInvariant() ?? "serial";
        ObsBatchExecution executionType = execution switch
        {
            "frame" => ObsBatchExecution.SerialFrame,
            "parallel" => ObsBatchExecution.Parallel,
            _ => ObsBatchExecution.SerialRealtime,
        };

        Result<IReadOnlyList<ObsResponse>> result = await Obs.RequestBatchAsync(
            ctx.BroadcasterId,
            new ObsRequestBatch(requests, executionType, GetBool(action, "halt_on_failure")),
            ctx.CancellationToken
        );
        if (result.IsFailure)
        {
            ctx.Variables["obs.last_error"] = result.ErrorMessage ?? "OBS batch failed";
            return ActionResult.Failure(result.ErrorMessage ?? "OBS batch failed");
        }
        int failed = result.Value.Count(r => !r.Ok);
        return failed == 0
            ? ActionResult.Success($"batch ok ({result.Value.Count} requests)")
            : ActionResult.Failure($"{failed}/{result.Value.Count} batch requests failed");
    }
}

/// <summary>Vendor plugin call. Config: <c>vendor</c>, <c>request_type</c>, <c>request_data?</c>.</summary>
public sealed class ObsCallVendorAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_call_vendor";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "vendor", out string vendor, out ActionResult vendorFailure))
            return vendorFailure;
        if (!TryRequire(ctx, action, "request_type", out string type, out ActionResult typeFailure))
            return typeFailure;
        Result<ObsResponse> response = await Obs.CallVendorAsync(
            ctx.BroadcasterId,
            vendor,
            type,
            ObsRequestAction.ReadDataObject(action, "request_data"),
            ctx.CancellationToken
        );
        if (response.IsFailure)
        {
            ctx.Variables["obs.last_error"] = response.ErrorMessage ?? "vendor call failed";
            return ActionResult.Failure(response.ErrorMessage ?? "vendor call failed");
        }
        return response.Value.Ok
            ? ActionResult.Success($"vendor '{vendor}' '{type}' ok")
            : ActionResult.Failure(response.Value.Error ?? "vendor call rejected");
    }
}
