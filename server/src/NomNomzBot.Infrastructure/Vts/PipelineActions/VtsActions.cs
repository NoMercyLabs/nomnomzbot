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
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;

namespace NomNomzBot.Infrastructure.Vts.PipelineActions;

/// <summary>
/// Shared plumbing for the six <c>vts_*</c> pipeline actions (vtube-studio.md §4): <c>{var}</c>
/// resolution, bool/double param readers, and the uniform failure mapping into
/// <c>ctx.Variables["vts.last_error"]</c>. Fail closed, mirror of the OBS action base.
/// </summary>
public abstract class VtsActionBase : ICommandAction
{
    protected VtsActionBase(IVtsControlService vts)
    {
        Vts = vts;
    }

    protected IVtsControlService Vts { get; }

    public abstract string ActionType { get; }

    public abstract Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    );

    protected static string? Param(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string key
    )
    {
        string? raw = action.GetString(key);
        if (raw is not null && raw.StartsWith('{') && raw.EndsWith('}'))
            ctx.Variables.TryGetValue(raw[1..^1], out raw);
        return raw;
    }

    protected static bool TryRequire(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string key,
        out string value,
        out ActionResult failure
    )
    {
        string? raw = Param(ctx, action, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = string.Empty;
            failure = ActionResult.Failure($"'{key}' is required");
            return false;
        }
        value = raw.Trim();
        failure = ActionResult.Success();
        return true;
    }

    protected static bool GetBool(ActionDefinition action, string key, bool defaultValue = false)
    {
        if (action.Parameters is null || !action.Parameters.TryGetValue(key, out JsonElement elem))
            return defaultValue;
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(elem.GetString(), out bool parsed)
                ? parsed
                : defaultValue,
            _ => defaultValue,
        };
    }

    protected static double? GetDouble(ActionDefinition action, string key)
    {
        if (action.Parameters is null || !action.Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.String => double.TryParse(
                elem.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed
            )
                ? parsed
                : null,
            _ => null,
        };
    }

    protected static ActionResult Map(PipelineExecutionContext ctx, Result result, string success)
    {
        if (result.IsSuccess)
            return ActionResult.Success(success);
        ctx.Variables["vts.last_error"] = result.ErrorMessage ?? "VTS request failed";
        return ActionResult.Failure(result.ErrorMessage ?? "VTS request failed");
    }
}

/// <summary>Load a model. Config: <c>model</c> (model id).</summary>
public sealed class VtsLoadModelAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_load_model";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "model", out string model, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Vts.LoadModelAsync(ctx.BroadcasterId, model, ctx.CancellationToken),
            $"model '{model}' loaded"
        );
    }
}

/// <summary>Trigger a hotkey. Config: <c>hotkey</c> (hotkey id or name).</summary>
public sealed class VtsTriggerHotkeyAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_trigger_hotkey";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "hotkey", out string hotkey, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Vts.TriggerHotkeyAsync(ctx.BroadcasterId, hotkey, ctx.CancellationToken),
            $"hotkey '{hotkey}' triggered"
        );
    }
}

/// <summary>Activate/deactivate an expression. Config: <c>expression</c> (file), <c>active</c>.</summary>
public sealed class VtsSetExpressionAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_set_expression";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "expression", out string expression, out ActionResult failure))
            return failure;
        bool active = GetBool(action, "active", defaultValue: true);
        return Map(
            ctx,
            await Vts.SetExpressionAsync(
                ctx.BroadcasterId,
                expression,
                active,
                ctx.CancellationToken
            ),
            $"expression '{expression}' {(active ? "activated" : "deactivated")}"
        );
    }
}

/// <summary>Move/scale/rotate the model. Config: <c>x?</c>, <c>y?</c>, <c>rotation?</c>, <c>size?</c>, <c>time_seconds?</c>, <c>relative?</c>.</summary>
public sealed class VtsMoveModelAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_move_model";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        VtsMove move = new(
            GetDouble(action, "x"),
            GetDouble(action, "y"),
            GetDouble(action, "rotation"),
            GetDouble(action, "size"),
            GetDouble(action, "time_seconds") ?? 0.3,
            GetBool(action, "relative")
        );
        if (move is { X: null, Y: null, Rotation: null, Size: null })
            return ActionResult.Failure("vts_move_model needs at least one of x/y/rotation/size");
        return Map(
            ctx,
            await Vts.MoveModelAsync(ctx.BroadcasterId, move, ctx.CancellationToken),
            "model moved"
        );
    }
}

/// <summary>Tint the model. Config: <c>r</c>, <c>g</c>, <c>b</c>, <c>a?</c>, <c>art_mesh_tag?</c>.</summary>
public sealed class VtsColorTintAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_color_tint";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        VtsColorTint tint = new(
            (byte)Math.Clamp(action.GetInt("r", 255), 0, 255),
            (byte)Math.Clamp(action.GetInt("g", 255), 0, 255),
            (byte)Math.Clamp(action.GetInt("b", 255), 0, 255),
            (byte)Math.Clamp(action.GetInt("a", 255), 0, 255),
            Param(ctx, action, "art_mesh_tag")
        );
        return Map(
            ctx,
            await Vts.ColorTintAsync(ctx.BroadcasterId, tint, ctx.CancellationToken),
            "model tinted"
        );
    }
}

/// <summary>Raw VTS API request. Config: <c>request_type</c>, <c>payload_json?</c>.</summary>
public sealed class VtsRequestAction(IVtsControlService vts) : VtsActionBase(vts)
{
    public override string ActionType => "vts_request";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "request_type", out string type, out ActionResult failure))
            return failure;
        Result<VtsRequestResult> result = await Vts.SendAsync(
            ctx.BroadcasterId,
            type,
            Param(ctx, action, "payload_json"),
            ctx.CancellationToken
        );
        if (result.IsFailure)
        {
            ctx.Variables["vts.last_error"] = result.ErrorMessage ?? "VTS request failed";
            return ActionResult.Failure(result.ErrorMessage ?? "VTS request failed");
        }
        if (!result.Value.Ok)
        {
            ctx.Variables["vts.last_error"] = result.Value.Error ?? "VTS rejected the request";
            return ActionResult.Failure(result.Value.Error ?? "VTS rejected the request");
        }
        ctx.Variables["vts.response"] = result.Value.DataJson ?? "{}";
        return ActionResult.Success($"'{type}' ok");
    }
}
