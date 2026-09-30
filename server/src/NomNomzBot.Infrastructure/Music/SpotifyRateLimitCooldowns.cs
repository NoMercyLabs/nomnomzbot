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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Per channel, the moment Spotify's <c>Retry-After</c> allows the next call. A singleton because the provider
/// is scoped: a deadline kept on the provider died with its scope, so every poll tick and dashboard request
/// started with no cooldown and called Spotify straight into another 429.
/// </summary>
public interface ISpotifyRateLimitCooldowns
{
    void CoolUntil(Guid broadcasterId, DateTimeOffset until);

    bool TryGetCoolingUntil(Guid broadcasterId, DateTimeOffset now, out DateTimeOffset until);
}

public sealed class SpotifyRateLimitCooldowns : ISpotifyRateLimitCooldowns
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _coolingUntil = new();

    public void CoolUntil(Guid broadcasterId, DateTimeOffset until) =>
        _coolingUntil[broadcasterId] = until;

    public bool TryGetCoolingUntil(Guid broadcasterId, DateTimeOffset now, out DateTimeOffset until)
    {
        if (_coolingUntil.TryGetValue(broadcasterId, out until) && until > now)
            return true;

        _coolingUntil.TryRemove(broadcasterId, out _);
        until = default;
        return false;
    }
}
