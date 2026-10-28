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

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Per-channel retry window for the moderator self-heal grant. The sweep that drives it runs every few minutes
/// for every channel; a grant Twitch keeps refusing (missing scope, the broadcaster removing the bot again) must
/// not become a call per sweep. Process-wide, so it is a singleton; a restart allows one fresh attempt.
/// </summary>
public sealed class BotModeratorGrantBackoff(TimeProvider clock)
{
    /// <summary>One attempt per channel per window, far below Twitch's add-moderator limit.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAttempt = new();

    /// <summary>True, and starts a new window, when no attempt happened for the channel inside the window.</summary>
    public bool TryEnter(Guid broadcasterId)
    {
        DateTimeOffset now = clock.GetUtcNow();
        bool entered = false;
        _lastAttempt.AddOrUpdate(
            broadcasterId,
            _ =>
            {
                entered = true;
                return now;
            },
            (_, last) =>
            {
                entered = now - last >= Window;
                return entered ? now : last;
            }
        );
        return entered;
    }
}
