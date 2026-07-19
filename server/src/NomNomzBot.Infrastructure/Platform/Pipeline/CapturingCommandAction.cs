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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.TestRun;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// The capture-mode stand-in for one side-effecting <see cref="ICommandAction"/> in a pipeline DRY-RUN
/// (commands-pipelines.md). It presents the SAME <see cref="ActionType"/> as the real action (so the engine resolves
/// it in place), but instead of performing the effect it RECORDS the step's action type + config into the shared
/// <see cref="CaptureSink"/> and returns success — so <c>{last.success}</c> is <c>true</c> and downstream branches run
/// as the happy path. For chat actions it resolves the message template for real (a read) and surfaces the resolved
/// text as captured chat output, WITHOUT sending it. The wrapped action is never invoked, so no external surface is
/// ever touched.
/// </summary>
public sealed class CapturingCommandAction(
    ICommandAction inner,
    CaptureSink sink,
    ITemplateResolver resolver
) : ICommandAction
{
    private static readonly JsonSerializerOptions PreviewOpts = new() { WriteIndented = false };

    public string ActionType => inner.ActionType;
    public string Category => inner.Category;
    public string Description => inner.Description;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string argsPreview = action.Parameters is { Count: > 0 }
            ? JsonSerializer.Serialize(action.Parameters, PreviewOpts)
            : "(no parameters)";
        sink.Record(inner.ActionType, argsPreview);

        // Chat actions carry the one piece of output an author most wants to eyeball. Resolve the template for real
        // (a read-only op) so the captured line reflects what chat WOULD have seen — but never dispatch it.
        if (inner.ActionType is "send_message" or "send_reply")
        {
            string template =
                action.GetString("message") ?? action.GetString("text") ?? string.Empty;
            if (!string.IsNullOrEmpty(template))
            {
                string resolved = await resolver.ResolveAsync(
                    template,
                    ctx.Variables,
                    ctx.BroadcasterId,
                    ctx.CancellationToken
                );
                sink.AddChatOutput(resolved);
                return ActionResult.Success(resolved);
            }
        }

        return ActionResult.Success($"captured:{inner.ActionType}");
    }
}
