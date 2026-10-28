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

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The variable keys a chat command always hands its pipeline and scripts — the set
/// <c>ChatMessageHandler.BuildInitialVariables</c> seeds. A test compares the two, so they cannot drift.
/// <c>args.N</c> is one-based and as long as the message, so it is a template literal type.
/// </summary>
internal static class ChatCommandVariableKeys
{
    /// <summary>The trigger key a script uses for a chat command.</summary>
    public const string Trigger = "command";

    public static readonly IReadOnlyList<string> Keys =
    [
        "args",
        "args.${number}",
        "args.count",
        "message",
        "target",
        "user",
        "user.bits",
        "user.id",
        "user.name",
        "user.provider",
        "user.role",
    ];
}
