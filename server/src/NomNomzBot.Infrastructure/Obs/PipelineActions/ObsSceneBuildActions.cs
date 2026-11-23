// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.PipelineActions;

// Actions that build OBS content: a scene, and a browser source inside one (Moderator floor, like the rest of OBS).

/// <summary>Create an empty OBS scene. Config: <c>scene_name</c>.</summary>
public sealed class ObsCreateSceneAction(IObsControlService obs) : ObsActionBase(obs)
{
    public override string ActionType => "obs_create_scene";

    public override LocalizedText Description => new("pipeline.obs_create_scene.description");
    public override IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "scene_name",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.obs_create_scene.scene_name.help")
            ),
        ];

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "scene_name", out string name, out ActionResult failure))
            return failure;
        return Map(
            ctx,
            await Obs.CreateSceneAsync(ctx.BroadcasterId, name, ctx.CancellationToken),
            $"scene '{name}' created"
        );
    }
}

/// <summary>
/// Add a browser source to a scene, or rewrite it when OBS already has one of that name. Config: <c>scene</c>,
/// <c>source_name</c>, <c>url</c>, <c>width</c>, <c>height</c>, <c>control_audio</c>, <c>shutdown_when_hidden</c>.
/// </summary>
public sealed class ObsAddBrowserSourceAction(IObsControlService obs) : ObsActionBase(obs)
{
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;

    public override string ActionType => "obs_add_browser_source";

    public override LocalizedText Description => new("pipeline.obs_add_browser_source.description");
    public override IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "scene",
                PipelineActionFieldKind.ObsScene,
                Required: true,
                Description: new("pipeline.obs_add_browser_source.scene.help")
            ),
            new(
                "source_name",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.obs_add_browser_source.source_name.help")
            ),
            new(
                "url",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.obs_add_browser_source.url.help")
            ),
            new(
                "width",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.obs_add_browser_source.width.help")
            ),
            new(
                "height",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.obs_add_browser_source.height.help")
            ),
            new(
                "control_audio",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.obs_add_browser_source.control_audio.help")
            ),
            new(
                "shutdown_when_hidden",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.obs_add_browser_source.shutdown_when_hidden.help")
            ),
        ];

    public override async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        if (!TryRequire(ctx, action, "scene", out string scene, out ActionResult sceneFailure))
            return sceneFailure;
        if (
            !TryRequire(
                ctx,
                action,
                "source_name",
                out string sourceName,
                out ActionResult nameFailure
            )
        )
            return nameFailure;
        if (!TryRequire(ctx, action, "url", out string url, out ActionResult urlFailure))
            return urlFailure;

        int width = action.GetInt("width", DefaultWidth);
        int height = action.GetInt("height", DefaultHeight);
        Result<ObsBrowserSourceOutcome> result = await Obs.UpsertBrowserSourceAsync(
            ctx.BroadcasterId,
            new(
                scene,
                sourceName,
                url,
                width > 0 ? width : DefaultWidth,
                height > 0 ? height : DefaultHeight,
                GetBool(action, "control_audio"),
                GetBool(action, "shutdown_when_hidden")
            ),
            ctx.CancellationToken
        );
        if (result.IsFailure)
            return FailFrom(ctx, result, "OBS request failed");

        return ActionResult.Success(
            result.Value == ObsBrowserSourceOutcome.Created
                ? $"browser source '{sourceName}' added to '{scene}'"
                : $"browser source '{sourceName}' updated"
        );
    }
}
