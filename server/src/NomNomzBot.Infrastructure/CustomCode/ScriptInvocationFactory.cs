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

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The one place a chat-triggered script run is described to the runner: the words after the command become the
/// args, the variables are copied (the script writes to its own copy), and a chat message id makes the run
/// threadable (<c>chat.reply</c>). The <c>run_code</c> pipeline step and a command that runs a script directly
/// both build their <see cref="ScriptInvocation"/> here.
/// </summary>
public static class ScriptInvocationFactory
{
    /// <summary>The words after the command word in a raw chat line (the first word is the command itself).</summary>
    public static IReadOnlyList<string> ArgsAfterCommand(string rawMessage) =>
        [.. rawMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1)];

    /// <summary>The words of an argument string that already has the command word cut off.</summary>
    public static IReadOnlyList<string> ArgsOf(string args) =>
        [.. args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    public static ScriptInvocation Create(
        string executionId,
        string triggeredByUserId,
        string triggeredByDisplayName,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> variables,
        string? messageId,
        string chatName
    ) =>
        new(
            executionId,
            triggeredByUserId,
            triggeredByDisplayName,
            args,
            new Dictionary<string, string>(variables),
            string.IsNullOrEmpty(messageId) ? null : new ScriptReplyTarget(messageId, chatName)
        );
}
