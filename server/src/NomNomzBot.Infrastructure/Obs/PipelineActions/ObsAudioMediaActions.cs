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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.PipelineActions;

// Audio, media, hotkey, browser, screenshot, replay-clip actions (obs-control.md §5, Moderator floor).

/// <summary>Mute/unmute/toggle an input. Config: <c>input</c>, <c>muted</c> or <c>toggle: true</c>.</summary>
public sealed class ObsInputMuteAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_input_mute";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "input", out string input, out ActionResult failure))
            return failure;
        if (GetBool(action, "toggle"))
            return Map(
                ctx,
                await Obs.ToggleInputMuteAsync(ctx.BroadcasterId, input, ctx.CancellationToken),
                $"toggled mute on '{input}'"
            );
        bool muted = GetBool(action, "muted", defaultValue: true);
        return Map(
            ctx,
            await Obs.SetInputMuteAsync(ctx.BroadcasterId, input, muted, ctx.CancellationToken),
            $"'{input}' {(muted ? "muted" : "unmuted")}"
        );
    }
}

/// <summary>Set an input's volume. Config: <c>input</c>, <c>volume_db</c> XOR <c>volume_mul</c>.</summary>
public sealed class ObsInputVolumeAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_input_volume";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "input", out string input, out ActionResult failure))
            return failure;
        double? volumeDb = GetDouble(action, "volume_db");
        double? volumeMul = GetDouble(action, "volume_mul");
        return Map(
            ctx,
            await Obs.SetInputVolumeAsync(
                ctx.BroadcasterId,
                input,
                volumeDb,
                volumeMul,
                ctx.CancellationToken
            ),
            $"volume set on '{input}'"
        );
    }
}

/// <summary>Drive a media input. Config: <c>input</c>, <c>action: play|pause|stop|restart|next|previous</c>.</summary>
public sealed class ObsMediaAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_media";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "input", out string input, out ActionResult failure))
            return failure;
        string verb = Param(ctx, action, "action")?.Trim().ToLowerInvariant() ?? "play";
        MediaAction media = verb switch
        {
            "play" => MediaAction.Play,
            "pause" => MediaAction.Pause,
            "stop" => MediaAction.Stop,
            "restart" => MediaAction.Restart,
            "next" => MediaAction.Next,
            "previous" => MediaAction.Previous,
            _ => MediaAction.Play,
        };
        return Map(
            ctx,
            await Obs.TriggerMediaAsync(ctx.BroadcasterId, input, media, ctx.CancellationToken),
            $"media '{verb}' on '{input}'"
        );
    }
}

/// <summary>Fire an OBS hotkey by name. Config: <c>hotkey_name</c>.</summary>
public sealed class ObsHotkeyAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_hotkey";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "hotkey_name", out string hotkey, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Obs.TriggerHotkeyAsync(ctx.BroadcasterId, hotkey, ctx.CancellationToken),
            $"hotkey '{hotkey}' triggered"
        );
    }
}

/// <summary>Refresh a browser source (no cache). Config: <c>input</c>.</summary>
public sealed class ObsRefreshBrowserAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_refresh_browser";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "input", out string input, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Obs.RefreshBrowserAsync(ctx.BroadcasterId, input, ctx.CancellationToken),
            $"browser source '{input}' refreshed"
        );
    }
}

/// <summary>Screenshot a source; the base64 lands in <c>obs.screenshot</c>. Config: <c>source</c>, <c>format</c>.</summary>
public sealed class ObsScreenshotAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_screenshot";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "source", out string source, out ActionResult failure))
            return failure;
        string format = Param(ctx, action, "format")?.Trim() ?? "png";
        Application.Common.Models.Result<string> shot = await Obs.ScreenshotAsync(
            ctx.BroadcasterId,
            source,
            format,
            ctx.CancellationToken
        );
        if (shot.IsFailure)
        {
            ctx.Variables["obs.last_error"] = shot.ErrorMessage ?? "screenshot failed";
            return ActionResult.Failure(shot.ErrorMessage ?? "screenshot failed");
        }
        ctx.Variables["obs.screenshot"] = shot.Value;
        return ActionResult.Success($"screenshot of '{source}' captured");
    }
}

/// <summary>Save the replay buffer (clip it). No config.</summary>
public sealed class ObsSaveReplayAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_save_replay";

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    ) =>
        Map(
            ctx,
            await Obs.SaveReplayBufferAsync(ctx.BroadcasterId, ctx.CancellationToken),
            "replay buffer saved"
        );
}
