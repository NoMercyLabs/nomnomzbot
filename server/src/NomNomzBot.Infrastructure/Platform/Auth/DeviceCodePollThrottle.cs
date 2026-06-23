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

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Per-device-code poll rate limiter (singleton): Twitch's Device Code Flow requires waiting the advertised
/// <c>interval</c> seconds between token polls, and slams back <c>slow_down</c> otherwise. The dashboard drives the
/// poll loop, so a fast/buggy client — or several clients polling the same code — could otherwise hammer Twitch
/// on our shared client id. This guard makes the bot a good API citizen unconditionally: it never forwards a poll
/// to Twitch sooner than the interval for a given code, returning a pending result instead. In-memory and bounded
/// (codes self-expire in ~30 min; stale entries are pruned), which is right for the self-host single node; the SaaS
/// multi-node variant would back this with the shared cache.
/// </summary>
public sealed class DeviceCodePollThrottle
{
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(35);

    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPollByCode = new(
        StringComparer.Ordinal
    );

    public DeviceCodePollThrottle(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <summary>
    /// Returns true when at least <paramref name="minInterval"/> has elapsed since the last forwarded poll for
    /// <paramref name="deviceCode"/> — and atomically records "now" as the new last-poll. Returns false when polled
    /// too soon, so the caller must NOT call Twitch (it reports the code as still pending). Thread-safe under
    /// concurrent polls of the same code.
    /// </summary>
    public bool TryAcquire(string deviceCode, TimeSpan minInterval)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        Prune(now);

        while (true)
        {
            if (_lastPollByCode.TryGetValue(deviceCode, out DateTimeOffset last))
            {
                if (now - last < minInterval)
                    return false;
                if (_lastPollByCode.TryUpdate(deviceCode, now, last))
                    return true;
            }
            else if (_lastPollByCode.TryAdd(deviceCode, now))
            {
                return true;
            }
            // A racing poll updated the entry first — re-read and re-decide.
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, DateTimeOffset> entry in _lastPollByCode)
            if (now - entry.Value > EntryTtl)
                _lastPollByCode.TryRemove(entry.Key, out _);
    }
}
