// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
