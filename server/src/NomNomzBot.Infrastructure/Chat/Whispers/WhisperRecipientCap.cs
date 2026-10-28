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
