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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation.PipelineActions;

public sealed class TimeoutAction : ICommandAction
{
    private readonly IPipelineModerationService _moderation;

    public string ActionType => "timeout";

    public LocalizedText Category => new("pipeline.category.moderation");

    public LocalizedText Description => new("pipeline.timeout.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "user_id",
                PipelineActionFieldKind.TwitchUser,
                Description: new("pipeline.timeout.user_id.help")
            ),
            new(
                "duration",
                PipelineActionFieldKind.Number,
                Required: true,
                Description: new("pipeline.timeout.duration.help")
            ),
            new(
                "reason",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.timeout.reason.help")
            ),
        ];

    public TimeoutAction(IPipelineModerationService moderation) => _moderation = moderation;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string userId =
            action.GetString("user_id")
            ?? ctx.Variables.GetValueOrDefault("target.id")
            ?? ctx.Variables.GetValueOrDefault("user.id")
            ?? string.Empty;

        if (string.IsNullOrEmpty(userId))
            return ActionResult.Failure("timeout: user_id not resolved");

        int duration = action.GetInt("duration", 60);
        if (duration <= 0)
            return ActionResult.Failure(
                $"timeout: duration must be a positive number of seconds, got {duration}"
            );

        string? reason = action.GetString("reason");

        Result<ModerationActionResult> timedOut = await _moderation.TimeoutAsync(
            ctx.BroadcasterId,
            userId,
            duration,
            reason,
            ctx.CancellationToken
        );
        return timedOut.IsSuccess
            ? ActionResult.Success($"Timed out {userId} for {duration}s")
            : ActionResult.Failure($"timeout: {timedOut.ErrorMessage}");
    }
}
