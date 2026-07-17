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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Process-wide (singleton) implementation of <see cref="IHelixBadgeSendGate"/>: a per-broadcaster
/// block-until timestamp. The 10-minute TTL keeps the state self-healing — after a broadcaster grants
/// <c>channel:bot</c> or mods the bot, the next window's first send re-proves eligibility.
/// </summary>
public sealed class HelixBadgeSendGate : IHelixBadgeSendGate
{
    private static readonly TimeSpan BlockTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _blockedUntil = new();
    private readonly TimeProvider _timeProvider;

    public HelixBadgeSendGate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool IsBlocked(Guid broadcasterId)
    {
        if (!_blockedUntil.TryGetValue(broadcasterId, out DateTimeOffset until))
            return false;

        if (until > _timeProvider.GetUtcNow())
            return true;

        _blockedUntil.TryRemove(broadcasterId, out _); // expired — drop the stale entry
        return false;
    }

    public void Block(Guid broadcasterId) =>
        _blockedUntil[broadcasterId] = _timeProvider.GetUtcNow().Add(BlockTtl);

    public void Clear(Guid broadcasterId) => _blockedUntil.TryRemove(broadcasterId, out _);
}
