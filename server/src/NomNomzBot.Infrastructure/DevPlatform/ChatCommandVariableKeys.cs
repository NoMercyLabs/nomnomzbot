// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
        "target",
        "user",
        "user.bits",
        "user.id",
        "user.name",
        "user.provider",
        "user.role",
    ];
}
