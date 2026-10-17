// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The in-memory <see cref="IBotModerationMarks"/>. A mark lives long enough for Twitch's EventSub notice to
/// arrive, then expires, so a stale mark never makes a later manual ban speak.
/// </summary>
public sealed class BotModerationMarks(TimeProvider clock) : IBotModerationMarks
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _marks = new();

    public void Mark(string targetUserId, string action)
    {
        DateTimeOffset now = clock.GetUtcNow();
        foreach (KeyValuePair<string, DateTimeOffset> mark in _marks)
            if (mark.Value <= now)
                _marks.TryRemove(mark.Key, out _);

        _marks[KeyOf(targetUserId, action)] = now + Lifetime;
    }

    public bool Consume(string targetUserId, string action) =>
        _marks.TryRemove(KeyOf(targetUserId, action), out DateTimeOffset expires)
        && expires > clock.GetUtcNow();

    private static string KeyOf(string targetUserId, string action) => $"{action}:{targetUserId}";
}
