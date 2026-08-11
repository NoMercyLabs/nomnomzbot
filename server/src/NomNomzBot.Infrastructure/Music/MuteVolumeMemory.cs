// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Cache-backed implementation of the per-channel pre-mute volume memory. Stored under
/// <c>music:premute:{broadcasterId}</c> with a rolling TTL — long enough that a mute followed by an
/// unmute minutes or hours later still restores correctly, short enough that a value from a stale
/// session eventually ages out rather than surprising a future unmute.
/// </summary>
public sealed class MuteVolumeMemory : IMuteVolumeMemory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly ICacheService _cache;

    public MuteVolumeMemory(ICacheService cache)
    {
        _cache = cache;
    }

    public Task RememberAsync(
        Guid broadcasterId,
        int volumePercent,
        CancellationToken ct = default
    ) => _cache.SetAsync(Key(broadcasterId), volumePercent, Ttl, ct);

    public Task<int?> GetAsync(Guid broadcasterId, CancellationToken ct = default) =>
        _cache.GetAsync<int?>(Key(broadcasterId), ct);

    private static string Key(Guid broadcasterId) => $"music:premute:{broadcasterId}";
}
