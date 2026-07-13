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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Quotes.PipelineActions;

/// <summary>
/// Posts a channel quote to chat (quotes.md §4). Config <c>{ number:int? }</c> — omitted/null posts a random
/// quote (quote-of-the-day on a timer), a value posts that specific quote. The rendered line is stowed in
/// <c>ctx.Variables["quote"]</c> for downstream steps and sent to chat. Fails closed when the channel has no
/// quotes (random) or the requested number is missing.
/// </summary>
public sealed class PostQuoteAction : ICommandAction
{
    private readonly IQuoteService _quotes;
    private readonly IChatProvider _chat;

    public string ActionType => "post_quote";

    public PostQuoteAction(IQuoteService quotes, IChatProvider chat)
    {
        _quotes = quotes;
        _chat = chat;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        int? number = ReadNumber(ctx, action);

        Result<QuoteDto> result = number is null
            ? await _quotes.GetRandomAsync(ctx.BroadcasterId, ctx.CancellationToken)
            : await _quotes.GetAsync(ctx.BroadcasterId, number.Value, ctx.CancellationToken);

        if (result.IsFailure)
            return ActionResult.Failure(
                result.ErrorMessage ?? "post_quote could not resolve a quote"
            );

        string line = QuoteFormatter.Format(result.Value);
        ctx.Variables["quote"] = line;

        await _chat.SendMessageAsync(ctx.BroadcasterId, line, ctx.CancellationToken);
        return ActionResult.Success(line);
    }

    /// <summary>
    /// Resolves which quote to post. A static <c>number</c> config value wins (a quote-of-the-day timer). With no
    /// config number, the triggering chat argument is honored — so a <c>!quote 5</c> command whose pipeline has a
    /// <c>post_quote</c> step posts quote #5 (the arg lands in <c>ctx.Variables["args.0"]</c>). Neither present
    /// (or a non-numeric value) means "random".
    /// </summary>
    private static int? ReadNumber(PipelineExecutionContext ctx, ActionDefinition action)
    {
        if (
            action.Parameters is not null
            && action.Parameters.TryGetValue("number", out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out int configured)
        )
            return configured;

        if (
            ctx.Variables.TryGetValue("args.0", out string? arg)
            && int.TryParse(arg, out int fromArg)
        )
            return fromArg;

        return null;
    }
}
