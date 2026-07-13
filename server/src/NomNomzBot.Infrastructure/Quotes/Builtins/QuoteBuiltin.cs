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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;

namespace NomNomzBot.Infrastructure.Quotes.Builtins;

/// <summary>
/// <c>!quote</c> — the full chat surface for the quote library (quotes.md §4, D2). Reading is open to
/// everyone; the mutating sub-commands gate on the SAME capability keys the REST surface uses, so chat is a
/// second surface and never a bypass:
/// <list type="bullet">
///   <item><c>!quote</c> → a random quote; <c>!quote &lt;n&gt;</c> → that quote (Everyone).</item>
///   <item><c>!quote add &lt;text&gt;</c> → a new quote (<c>quotes:write</c>). Used as a REPLY with no text it
///   captures the replied-to message and attributes it to that user — the natural way to quote someone.</item>
///   <item><c>!quote edit|update &lt;n&gt; &lt;text&gt;</c> → re-body a quote, keeping its attribution
///   (<c>quotes:write</c>).</item>
///   <item><c>!quote del|delete|remove &lt;n&gt;</c> → soft-delete a quote (<c>quotes:delete</c>).</item>
/// </list>
/// Always replies (never a silent no-op) and renders through the shared <see cref="QuoteFormatter"/> so the
/// line matches the <c>post_quote</c> pipeline action.
/// </summary>
public sealed class QuoteBuiltin : IBuiltinCommand
{
    private const string WriteCapability = "quotes:write";
    private const string DeleteCapability = "quotes:delete";

    private readonly IQuoteService _quotes;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;

    public QuoteBuiltin(IQuoteService quotes, IUserService users, IRoleResolver roles)
    {
        _quotes = quotes;
        _users = users;
        _roles = roles;
    }

    public string BuiltinKey => "quote";
    public int DefaultCooldownSeconds => 5;

    // Read is Everyone; the mutating sub-commands self-gate on the quotes:write / quotes:delete capability.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        (string verb, string rest) = SplitVerb(context.Args);

        return verb switch
        {
            "add" => await AddAsync(context, rest, ct),
            "edit" or "update" => await EditAsync(context, rest, ct),
            "del" or "delete" or "remove" => await DeleteAsync(context, rest, ct),
            // Anything else (empty, a number, or a stray word) is a read — a number posts that quote, else random.
            _ => await ReadAsync(context, ct),
        };
    }

    /// <summary><c>!quote</c> / <c>!quote &lt;n&gt;</c> — always replies, even when the quote/library is absent.</summary>
    private async Task<Result<string>> ReadAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        int? number = ParseNumber(context.Args);

        Result<QuoteDto> result = number is null
            ? await _quotes.GetRandomAsync(context.BroadcasterId, ct)
            : await _quotes.GetAsync(context.BroadcasterId, number.Value, ct);

        if (result.IsSuccess)
            return Result.Success(QuoteFormatter.Format(result.Value));

        // A miss is never silence — distinguish "no quotes yet" from "that number doesn't exist".
        return Result.Success(
            number is null ? "There are no quotes yet." : $"I couldn't find quote #{number.Value}."
        );
    }

    /// <summary>
    /// <c>!quote add &lt;text&gt;</c>. As a reply with no text it captures the replied-to message and attributes
    /// it to that user; otherwise the quote is the text typed after <c>add</c>.
    /// </summary>
    private async Task<Result<string>> AddAsync(
        BuiltinCommandContext context,
        string rest,
        CancellationToken ct
    )
    {
        Result<Guid> invoker = await AuthorizeAsync(context, WriteCapability, "add quotes", ct);
        if (invoker.IsFailure)
            return Result.Success(invoker.ErrorMessage!);

        string? replyBody = context.ReplyParentMessageBody?.Trim();
        bool captureReply = !string.IsNullOrWhiteSpace(replyBody);

        // A reply target is unambiguous — quote it and attribute to its author, ignoring any trailing text.
        string text = captureReply ? replyBody! : rest.Trim();
        string? attribution = captureReply ? context.ReplyParentUserName : null;

        if (string.IsNullOrWhiteSpace(text))
            return Result.Success(
                "Usage: !quote add <text> — or reply to a message with !quote add."
            );

        Result<QuoteDto> added = await _quotes.AddAsync(
            context.BroadcasterId,
            new AddQuoteRequest(text, attribution, null, null, invoker.Value),
            ct
        );

        return Result.Success(
            added.IsSuccess
                ? $"Added {QuoteFormatter.Format(added.Value)}"
                : added.ErrorMessage ?? "I couldn't add that quote."
        );
    }

    /// <summary><c>!quote edit|update &lt;n&gt; &lt;text&gt;</c> — re-bodies a quote, keeping its attribution.</summary>
    private async Task<Result<string>> EditAsync(
        BuiltinCommandContext context,
        string rest,
        CancellationToken ct
    )
    {
        Result<Guid> invoker = await AuthorizeAsync(context, WriteCapability, "edit quotes", ct);
        if (invoker.IsFailure)
            return Result.Success(invoker.ErrorMessage!);

        (int? number, string text) = SplitNumberAndText(rest);
        if (number is null || string.IsNullOrWhiteSpace(text))
            return Result.Success("Usage: !quote edit <number> <new text>");

        // EditAsync replaces the whole record, so carry the existing attribution forward — a chat edit changes
        // only the wording.
        Result<QuoteDto> existing = await _quotes.GetAsync(context.BroadcasterId, number.Value, ct);
        if (existing.IsFailure)
            return Result.Success($"I couldn't find quote #{number.Value}.");

        Result<QuoteDto> edited = await _quotes.EditAsync(
            context.BroadcasterId,
            number.Value,
            new EditQuoteRequest(
                text.Trim(),
                existing.Value.QuotedDisplayName,
                existing.Value.ContextGame
            ),
            ct
        );

        return Result.Success(
            edited.IsSuccess
                ? $"Updated {QuoteFormatter.Format(edited.Value)}"
                : edited.ErrorMessage ?? "I couldn't update that quote."
        );
    }

    /// <summary><c>!quote del|delete|remove &lt;n&gt;</c> — soft-deletes a quote; its number is never reused.</summary>
    private async Task<Result<string>> DeleteAsync(
        BuiltinCommandContext context,
        string rest,
        CancellationToken ct
    )
    {
        Result<Guid> invoker = await AuthorizeAsync(context, DeleteCapability, "delete quotes", ct);
        if (invoker.IsFailure)
            return Result.Success(invoker.ErrorMessage!);

        (int? number, _) = SplitNumberAndText(rest);
        if (number is null)
            return Result.Success("Usage: !quote del <number>");

        Result deleted = await _quotes.DeleteAsync(context.BroadcasterId, number.Value, ct);
        return Result.Success(
            deleted.IsSuccess
                ? $"Deleted quote #{number.Value}."
                : $"I couldn't find quote #{number.Value}."
        );
    }

    /// <summary>
    /// Resolves the invoking chatter to their internal user id (the get-or-create viewer seam) and requires
    /// the given capability. Fails closed with a friendly, role-name-free message — the streamer may have
    /// lowered the floor, so it never claims a specific role.
    /// </summary>
    private async Task<Result<Guid>> AuthorizeAsync(
        BuiltinCommandContext context,
        string capability,
        string verb,
        CancellationToken ct
    )
    {
        Result<UserDto> invoker = await _users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (invoker.IsFailure || !Guid.TryParse(invoker.Value.Id, out Guid invokerId))
            return Result.Failure<Guid>(
                "Your account could not be resolved — try again.",
                "NOT_FOUND"
            );

        Result<bool> allowed = await _roles.HasCapabilityAsync(
            invokerId,
            context.BroadcasterId,
            capability,
            ct
        );
        return allowed.IsSuccess && allowed.Value
            ? Result.Success(invokerId)
            : Result.Failure<Guid>($"You don't have permission to {verb}.", "FORBIDDEN");
    }

    /// <summary>Splits args into a lowercase leading verb and the trimmed remainder.</summary>
    private static (string Verb, string Remainder) SplitVerb(string args)
    {
        string trimmed = args.Trim();
        int space = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed.ToLowerInvariant(), string.Empty)
            : (trimmed[..space].ToLowerInvariant(), trimmed[(space + 1)..].Trim());
    }

    /// <summary>The first whitespace-delimited token of <paramref name="args"/> as a quote number, or null.</summary>
    private static int? ParseNumber(string args)
    {
        string first =
            args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return int.TryParse(first, out int number) ? number : null;
    }

    /// <summary>Splits "&lt;number&gt; &lt;text…&gt;" into the leading number (or null) and the trailing text.</summary>
    private static (int? Number, string Text) SplitNumberAndText(string rest)
    {
        string trimmed = rest.Trim();
        int space = trimmed.IndexOf(' ');
        string head = space < 0 ? trimmed : trimmed[..space];
        string tail = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        return int.TryParse(head, out int number) ? (number, tail) : (null, tail);
    }
}
