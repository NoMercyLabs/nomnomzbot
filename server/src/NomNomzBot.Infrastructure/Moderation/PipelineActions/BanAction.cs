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

public sealed class BanAction : ICommandAction
{
    private readonly IPipelineModerationService _moderation;

    public string ActionType => "ban";

    public LocalizedText Category => new("pipeline.category.moderation");

    public LocalizedText Description => new("pipeline.ban.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "user_id",
                PipelineActionFieldKind.TwitchUser,
                Description: new("pipeline.ban.user_id.help")
            ),
            new(
                "reason",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.ban.reason.help")
            ),
        ];

    public BanAction(IPipelineModerationService moderation) => _moderation = moderation;

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
            return ActionResult.Failure("ban: user_id not resolved");

        string? reason = action.GetString("reason");
        Result<ModerationActionResult> banned = await _moderation.BanAsync(
            ctx.BroadcasterId,
            userId,
            reason,
            ctx.CancellationToken
        );
        return banned.IsSuccess
            ? ActionResult.Success($"Banned {userId}")
            : ActionResult.Failure($"ban: {banned.ErrorMessage}");
    }
}
