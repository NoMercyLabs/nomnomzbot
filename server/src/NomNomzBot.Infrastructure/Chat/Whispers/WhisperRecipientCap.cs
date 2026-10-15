// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Chat.Whispers;

/// <summary>
/// Twitch lets an account whisper only a limited number of different new people per day. This counts the
/// distinct recipients the bot has whispered in the current UTC day: someone already counted is always
/// admitted, a new one only while the day's count is under <see cref="DailyNewRecipientLimit"/>.
/// </summary>
public sealed class WhisperRecipientCap(TimeProvider clock)
{
    // Helix Send Whisper: "You may whisper to a maximum of 40 unique recipients per day."
    public const int DailyNewRecipientLimit = 40;

    private readonly object _gate = new();
    private readonly HashSet<string> _today = [];
    private DateOnly _day;

    public bool TryAdmit(string recipientTwitchUserId)
    {
        DateOnly now = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        lock (_gate)
        {
            if (now != _day)
            {
                _today.Clear();
                _day = now;
            }

            if (_today.Contains(recipientTwitchUserId))
                return true;
            if (_today.Count >= DailyNewRecipientLimit)
                return false;
            _today.Add(recipientTwitchUserId);
            return true;
        }
    }
}
