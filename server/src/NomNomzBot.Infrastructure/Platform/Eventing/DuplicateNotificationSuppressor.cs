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
using System.Security.Cryptography;
using System.Text;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// In-process, time-bounded implementation of <see cref="IDuplicateNotificationSuppressor"/>. Singleton (the
/// claim ledger must outlive the per-notification scope <see cref="NotificationDispatcher"/> is resolved in)
/// and lock-free: a claim is a single atomic dictionary operation, safe under the concurrent notification
/// traffic multiple broadcaster sessions produce. Sized to a short reconnect-window guard, not a durable
/// store — a claim expires and is swept the first time a call lands after its window closes, so the ledger
/// never grows past the notification volume of one window.
/// </summary>
public sealed class DuplicateNotificationSuppressor : IDuplicateNotificationSuppressor
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _claims = new();

    // Bounds the sweep cost: only walk the whole table every Nth claim rather than on every call.
    private int _callsSinceSweep;
    private const int SweepEveryNCalls = 256;

    public bool TryClaim(
        Guid broadcasterId,
        string subscriptionType,
        string rawPayloadJson,
        DateTimeOffset now,
        TimeSpan window
    )
    {
        string key = BuildKey(broadcasterId, subscriptionType, rawPayloadJson);

        while (true)
        {
            if (_claims.TryGetValue(key, out DateTimeOffset expiresAt))
            {
                if (expiresAt > now)
                    return false; // still within the window — semantic duplicate

                // Expired: try to renew it as a fresh claim. A lost race just retries the read.
                if (_claims.TryUpdate(key, now + window, expiresAt))
                {
                    MaybeSweep(now);
                    return true;
                }

                continue;
            }

            if (_claims.TryAdd(key, now + window))
            {
                MaybeSweep(now);
                return true;
            }

            // Another thread claimed it between the TryGetValue miss and this TryAdd — retry the read.
        }
    }

    private void MaybeSweep(DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _callsSinceSweep) < SweepEveryNCalls)
            return;

        Interlocked.Exchange(ref _callsSinceSweep, 0);
        foreach ((string key, DateTimeOffset expiresAt) in _claims)
        {
            if (expiresAt <= now)
                _claims.TryRemove(key, out _);
        }
    }

    private static string BuildKey(
        Guid broadcasterId,
        string subscriptionType,
        string rawPayloadJson
    )
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{broadcasterId:D}|{subscriptionType}|{rawPayloadJson}")
        );
        return Convert.ToHexStringLower(hash);
    }
}
