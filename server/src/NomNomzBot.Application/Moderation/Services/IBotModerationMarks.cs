// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
