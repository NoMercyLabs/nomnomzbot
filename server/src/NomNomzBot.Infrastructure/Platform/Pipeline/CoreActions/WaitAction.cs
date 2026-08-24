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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;

/// <summary>
/// <c>milliseconds</c>/<c>seconds</c> accept a template — <c>{{tts.durationMs}}</c> from a prior
/// <c>tts_synthesize</c> step, <c>{{last.output}}</c>, or a plain literal — so a pipeline can wait for a
/// length only known at execution time, not just a fixed number authored in advance. Capped at 30s per
/// step (a runaway/misconfigured template must not hang a pipeline execution indefinitely); chain
/// multiple <c>wait</c> steps for a longer total.
/// </summary>
public sealed class WaitAction : ICommandAction
{
    private readonly ITemplateResolver _resolver;

    public string ActionType => "wait";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new("milliseconds", PipelineActionFieldKind.Number),
            new("seconds", PipelineActionFieldKind.Number),
        ];

    public WaitAction(ITemplateResolver resolver) => _resolver = resolver;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        int ms = await ResolveIntAsync(ctx, action, "milliseconds");
        int seconds = await ResolveIntAsync(ctx, action, "seconds");
        int totalMs = ms + seconds * 1000;

        if (totalMs <= 0)
            return ActionResult.Success();
        if (totalMs > 30_000)
            totalMs = 30_000; // cap at 30s per step

        await Task.Delay(totalMs, ctx.CancellationToken);
        return ActionResult.Success();
    }

    private async Task<int> ResolveIntAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string key
    )
    {
        string? raw = action.GetString(key);
        if (string.IsNullOrWhiteSpace(raw))
            return action.GetInt(key, 0);

        string resolved = await _resolver.ResolveAsync(
            raw,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        return int.TryParse(resolved, out int value) ? value : 0;
    }
}
