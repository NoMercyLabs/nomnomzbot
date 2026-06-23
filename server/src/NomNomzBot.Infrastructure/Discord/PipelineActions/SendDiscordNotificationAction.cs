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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Infrastructure.Discord.PipelineActions;

/// <summary>
/// Posts a notification to a linked Discord channel from a command/event pipeline (discord.md §6) through the
/// same dispatch path (with dedupe). Config <c>{ trigger_type, dedupe_key? }</c> — the matching enabled
/// notification rule for <c>trigger_type</c> supplies the channel + template; the pipeline's variables become
/// the template data. The dedupe key defaults to <c>pipeline:{MessageId}</c> so re-running the same trigger
/// posts once. Returns the posted message id on <c>sent</c>/<c>skipped_dupe</c>, fails on <c>failed</c>; never
/// stops the pipeline.
/// </summary>
public sealed class SendDiscordNotificationAction : ICommandAction
{
    private readonly IDiscordNotificationDispatcher _dispatcher;

    public string ActionType => "send_discord_notification";

    public SendDiscordNotificationAction(IDiscordNotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? triggerType = action.GetString("trigger_type");
        if (string.IsNullOrWhiteSpace(triggerType))
            return ActionResult.Failure("send_discord_notification requires a trigger_type.");

        string dedupeKey =
            action.GetString("dedupe_key")
            ?? $"pipeline:{(string.IsNullOrEmpty(ctx.MessageId) ? ctx.ExecutionId : ctx.MessageId)}";

        // The pipeline's resolved variables become the dispatch template data (the dispatcher renders the
        // configured rule's template against them via ITemplateEngine).
        Dictionary<string, string> templateData = new(
            ctx.Variables,
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["user.name"] = ctx.TriggeredByDisplayName,
            ["raw.message"] = ctx.RawMessage,
        };

        Result<DiscordDispatchOutcomeDto> result = await _dispatcher.DispatchAsync(
            new DiscordDispatchRequest(
                ctx.BroadcasterId,
                triggerType,
                dedupeKey,
                StreamId: null,
                templateData
            ),
            ctx.CancellationToken
        );

        if (result.IsFailure)
            return ActionResult.Failure(
                result.ErrorMessage ?? "send_discord_notification failed to dispatch."
            );

        DiscordDispatchOutcomeDto outcome = result.Value;
        return outcome.Status == "failed"
            ? ActionResult.Failure(outcome.Error ?? "Discord post failed.")
            : ActionResult.Success(outcome.PostedMessageId);
    }
}
