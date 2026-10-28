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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Remembers the bans, timeouts and unbans the bot made on its own, so only those announce in chat.
/// Twitch signs every action with a person's token, so the event's moderator cannot tell them apart.
/// A ban a person made by hand gives the banned viewer no attention (Stoney, 2026-10-17).
/// </summary>
public interface IBotModerationMarks
{
    void Mark(string targetUserId, string action);

    /// <summary>True once for a marked action; the mark is used up so a later manual action stays quiet.</summary>
    bool Consume(string targetUserId, string action);
}

public static class BotModerationActions
{
    public const string Ban = "ban";
    public const string Timeout = "timeout";
    public const string Unban = "unban";
}
