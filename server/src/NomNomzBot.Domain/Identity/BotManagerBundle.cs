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

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// The Bot manager bundle (roles-permissions §0.3): a live <c>bot:manager:author</c> permit grant satisfies every
/// action key listed here. It is a capability bundle on its own axis, never a ladder rung. It deliberately
/// holds no moderation key, no OBS key, no channel-settings key and no role or permit management key.
/// An entry ending in <c>*</c> covers every key that starts with the text before it.
/// </summary>
public static class BotManagerBundle
{
    /// <summary>The bundle key a Broadcaster grants to a named user.</summary>
    public const string Key = "bot:manager:author";

    /// <summary>The fixed member list, exactly as written in the spec.</summary>
    public static readonly IReadOnlyList<string> Members =
    [
        "commands:*",
        "pipelines:*",
        "eventresponses:*",
        "chattriggers:*",
        "voicetriggers:*",
        "chatpolls:*",
        "timers:*",
        "widget:*",
        "sounds:*",
        "quotes:*",
        "sdk:read",
        "code:script:author",
        "bundles:read",
        "bundles:export",
        "bundles:import",
        "tts:config:*",
        "tts:voice:*",
        "music:config:*",
        "music:library:write",
    ];

    /// <summary>True when holding the bundle satisfies <paramref name="actionKey"/>.</summary>
    public static bool Covers(string actionKey)
    {
        foreach (string member in Members)
        {
            bool matches = member.EndsWith('*')
                ? actionKey.StartsWith(member[..^1], StringComparison.Ordinal)
                : string.Equals(actionKey, member, StringComparison.Ordinal);
            if (matches)
                return true;
        }
        return false;
    }
}
