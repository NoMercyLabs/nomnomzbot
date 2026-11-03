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

using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// The optional target of a "caller or someone else" public-data command (<c>!followage @name</c>,
/// <c>!accountage name</c>): the first word of the arguments, resolved to a Twitch login.
/// </summary>
internal static class TargetArgument
{
    /// <summary>
    /// The login the viewer asked about, or <c>null</c> when the command is about the caller: no argument, or
    /// the viewer named themselves.
    /// </summary>
    public static async Task<string?> ResolveLoginAsync(
        BuiltinCommandContext context,
        IMentionTargetResolver mentionTargets,
        CancellationToken ct
    )
    {
        string[] words = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        string login = await mentionTargets.ResolveLoginAsync(words[0], ct);
        if (login.Length == 0)
            return null;

        return string.Equals(login, context.TriggeringUserLogin, StringComparison.OrdinalIgnoreCase)
            ? null
            : login.ToLowerInvariant();
    }
}
