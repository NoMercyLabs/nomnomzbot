// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.PipelineActions;

// Scene + source-visibility actions (obs-control.md §5, Moderator floor).

/// <summary>Switch the program scene. Config: <c>scene</c>.</summary>
public sealed class ObsSwitchSceneAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_switch_scene";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "scene", out string scene, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Obs.SwitchSceneAsync(ctx.BroadcasterId, scene, ctx.CancellationToken),
            $"switched to scene '{scene}'"
        );
    }
}

/// <summary>Set the studio-mode preview scene. Config: <c>scene</c>.</summary>
public sealed class ObsSetPreviewSceneAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_set_preview_scene";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "scene", out string scene, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Obs.SetPreviewSceneAsync(ctx.BroadcasterId, scene, ctx.CancellationToken),
            $"preview scene set to '{scene}'"
        );
    }
}

/// <summary>Show/hide a source in a scene. Config: <c>scene</c>, <c>source</c>, <c>visible</c>.</summary>
public sealed class ObsSetSourceAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_set_source";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "scene", out string scene, out ActionResult sceneFailure))
            return sceneFailure;
        if (!TryRequire(ctx, action, "source", out string source, out ActionResult sourceFailure))
            return sourceFailure;
        bool visible = GetBool(action, "visible", defaultValue: true);
        return Map(
            ctx,
            await Obs.SetSourceVisibleAsync(
                ctx.BroadcasterId,
                scene,
                source,
                visible,
                ctx.CancellationToken
            ),
            $"source '{source}' {(visible ? "shown" : "hidden")} in '{scene}'"
        );
    }
}

/// <summary>Enable/disable a filter on a source. Config: <c>source</c>, <c>filter</c>, <c>enabled</c>.</summary>
public sealed class ObsFilterAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_filter";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "source", out string source, out ActionResult sourceFailure))
            return sourceFailure;
        if (!TryRequire(ctx, action, "filter", out string filter, out ActionResult filterFailure))
            return filterFailure;
        bool enabled = GetBool(action, "enabled", defaultValue: true);
        return Map(
            ctx,
            await Obs.SetFilterEnabledAsync(
                ctx.BroadcasterId,
                source,
                filter,
                enabled,
                ctx.CancellationToken
            ),
            $"filter '{filter}' {(enabled ? "enabled" : "disabled")} on '{source}'"
        );
    }
}

/// <summary>Set the current transition and/or fire a studio transition. Config: <c>transition?</c>, <c>studio</c>, <c>duration_ms?</c>.</summary>
public sealed class ObsTransitionAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_transition";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? transition = Param(ctx, action, "transition");
        if (!string.IsNullOrWhiteSpace(transition))
        {
            ActionResult set = Map(
                ctx,
                await Obs.SetCurrentTransitionAsync(
                    ctx.BroadcasterId,
                    transition.Trim(),
                    ctx.CancellationToken
                ),
                $"transition set to '{transition}'"
            );
            if (!set.Succeeded)
                return set;
        }

        if (GetBool(action, "studio"))
        {
            int duration = action.GetInt("duration_ms", -1);
            return Map(
                ctx,
                await Obs.TriggerStudioTransitionAsync(
                    ctx.BroadcasterId,
                    duration > 0 ? duration : null,
                    ctx.CancellationToken
                ),
                "studio transition triggered"
            );
        }
        return string.IsNullOrWhiteSpace(transition)
            ? ActionResult.Failure("obs_transition needs 'transition' and/or 'studio: true'")
            : ActionResult.Success($"transition set to '{transition}'");
    }
}
