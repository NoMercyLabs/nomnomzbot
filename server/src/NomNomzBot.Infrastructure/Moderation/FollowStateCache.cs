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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Remembers follow lookups per (broadcaster, user) and rations the Helix calls that fill it.
///
/// <para>Singleton on purpose: the spam handler runs in a fresh scope per chat message, so a cache held
/// on a scoped service would never hit.</para>
///
/// <para><b>Sizing against Helix.</b> Helix gives each user token 800 points a minute and one follower
/// lookup costs 1. A channel may spend at most <see cref="MaxLookupsPerChannelPerMinute"/> (300) of those
/// on follow lookups, leaving 500 a minute for everything else on the same token. At most
/// <see cref="MaxConcurrentLookups"/> (8) run at once across the instance. Over either limit the answer is
/// Unknown (never a wait, never "not following") and the miss is not cached, so a later message retries.
/// The cache holds at most <see cref="MaxEntries"/> (50 000) entries, about 5 MB.</para>
/// </summary>
public sealed class FollowStateCache
{
    public const int MaxEntries = 50_000;
    public const int MaxLookupsPerChannelPerMinute = 300;
    public const int MaxConcurrentLookups = 8;

    /// <summary>The follow date never changes, so a follower is remembered for a long time.</summary>
    public static readonly TimeSpan FollowerTtl = TimeSpan.FromHours(12);

    /// <summary>Short, so somebody who follows after their first message is seen within minutes.</summary>
    public static readonly TimeSpan NotFollowingTtl = TimeSpan.FromMinutes(3);

    /// <summary>Short, so a failing lookup (missing scope, Helix down) is not retried on every message.</summary>
    public static readonly TimeSpan UnknownTtl = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<(Guid Channel, string User), Entry> _entries = new();
    private readonly ConcurrentDictionary<Guid, Window> _windows = new();
    private readonly SemaphoreSlim _slots = new(MaxConcurrentLookups, MaxConcurrentLookups);

    public FollowStateCache(TimeProvider time)
    {
        _time = time;
    }

    public int Count => _entries.Count;

    public bool TryGet(Guid broadcasterId, string userId, out FollowLookup lookup)
    {
        if (
            _entries.TryGetValue((broadcasterId, userId), out Entry entry)
            && entry.ExpiresAt > _time.GetUtcNow()
        )
        {
            lookup = entry.Lookup;
            return true;
        }

        lookup = FollowLookup.Unknown;
        return false;
    }

    public void Set(Guid broadcasterId, string userId, FollowLookup lookup)
    {
        TimeSpan ttl = lookup.State switch
        {
            FollowState.Following => FollowerTtl,
            FollowState.NotFollowing => NotFollowingTtl,
            _ => UnknownTtl,
        };

        if (_entries.Count >= MaxEntries)
            MakeRoom();

        _entries[(broadcasterId, userId)] = new Entry(lookup, _time.GetUtcNow() + ttl);
    }

    /// <summary>Take a Helix lookup slot, or false when the channel budget or the concurrency cap is spent.</summary>
    public bool TryBeginLookup(Guid broadcasterId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool withinBudget = true;
        _windows.AddOrUpdate(
            broadcasterId,
            _ => new Window(now, 1),
            (_, window) =>
            {
                if (now - window.StartedAt >= TimeSpan.FromMinutes(1))
                    return new Window(now, 1);
                if (window.Count >= MaxLookupsPerChannelPerMinute)
                {
                    withinBudget = false;
                    return window;
                }

                return window with
                {
                    Count = window.Count + 1,
                };
            }
        );

        if (!withinBudget)
            return false;

        return _slots.Wait(0);
    }

    public void EndLookup() => _slots.Release();

    private void MakeRoom()
    {
        DateTimeOffset now = _time.GetUtcNow();
        foreach (KeyValuePair<(Guid Channel, string User), Entry> pair in _entries)
            if (pair.Value.ExpiresAt <= now)
                _entries.TryRemove(pair.Key, out _);

        if (_entries.Count < MaxEntries)
            return;

        // Still full of live entries: drop the ones closest to expiry, a tenth of the cache.
        foreach (
            (Guid Channel, string User) key in _entries
                .OrderBy(pair => pair.Value.ExpiresAt)
                .Take(MaxEntries / 10)
                .Select(pair => pair.Key)
                .ToList()
        )
            _entries.TryRemove(key, out _);
    }

    private readonly record struct Entry(FollowLookup Lookup, DateTimeOffset ExpiresAt);

    private sealed record Window(DateTimeOffset StartedAt, int Count);
}
