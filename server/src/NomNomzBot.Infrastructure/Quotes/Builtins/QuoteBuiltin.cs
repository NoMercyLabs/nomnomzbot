// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;

namespace NomNomzBot.Infrastructure.Quotes.Builtins;

/// <summary>
/// <c>!quote [number]</c> — posts a stored channel quote to chat. A number posts that specific quote
/// (<c>!quote 5</c>); no argument (or a non-numeric one) posts a random quote. Read-only — adding/editing quotes
/// is the dashboard's job — and open to everyone. Renders through the shared <see cref="QuoteFormatter"/> so the
/// line matches the <c>post_quote</c> pipeline action.
/// </summary>
public sealed class QuoteBuiltin : IBuiltinCommand
{
    private readonly IQuoteService _quotes;

    public QuoteBuiltin(IQuoteService quotes)
    {
        _quotes = quotes;
    }

    public string BuiltinKey => "quote";
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        int? number = ParseNumber(context.Args);

        Result<QuoteDto> result = number is null
            ? await _quotes.GetRandomAsync(context.BroadcasterId, ct)
            : await _quotes.GetAsync(context.BroadcasterId, number.Value, ct);

        if (result.IsSuccess)
            return Result.Success(QuoteFormatter.Format(result.Value));

        // Always reply — a silent no-op is exactly the bug this fixes. Distinguish "no quotes yet" from
        // "that number doesn't exist" so the viewer knows which.
        string message = number is null
            ? "There are no quotes yet."
            : $"I couldn't find quote #{number.Value}.";
        return Result.Success(message);
    }

    /// <summary>The first whitespace-delimited argument as a quote number, or null (post a random quote).</summary>
    private static int? ParseNumber(string args)
    {
        string first =
            args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return int.TryParse(first, out int number) ? number : null;
    }
}
