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
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.PipelineActions;

public sealed class TimeoutAction : ICommandAction
{
    private readonly IChatProvider _chat;

    public string ActionType => "timeout";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "user_id",
                PipelineActionFieldKind.TwitchUser,
                Description: new(
                    "pipeline.timeout.user_id.help",
                    "The viewer to time out. Blank targets the user who triggered this pipeline.",
                    "De kijker die een timeout krijgt. Leeg richt zich op de gebruiker die deze pipeline "
                        + "activeerde."
                )
            ),
            new(
                "duration",
                PipelineActionFieldKind.Number,
                Required: true,
                Description: new(
                    "pipeline.timeout.duration.help",
                    "How long the timeout lasts, in seconds.",
                    "Hoe lang de timeout duurt, in seconden."
                )
            ),
            new(
                "reason",
                PipelineActionFieldKind.Text,
                Description: new(
                    "pipeline.timeout.reason.help",
                    "Shown to the viewer and logged in the moderation history.",
                    "Wordt aan de kijker getoond en vastgelegd in de moderatiegeschiedenis."
                )
            ),
        ];

    public TimeoutAction(IChatProvider chat) => _chat = chat;

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

        await _chat.TimeoutUserAsync(
            ctx.BroadcasterId,
            userId,
            duration,
            reason,
            ctx.CancellationToken
        );
        return ActionResult.Success($"Timed out {userId} for {duration}s");
    }
}
