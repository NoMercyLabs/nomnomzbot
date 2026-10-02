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
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Per channel, the moment Spotify's <c>Retry-After</c> allows the next call. A singleton because the provider
/// is scoped: a deadline kept on the provider died with its scope, so every poll tick and dashboard request
/// started with no cooldown and called Spotify straight into another 429.
/// </summary>
public interface ISpotifyRateLimitCooldowns
{
    void CoolUntil(Guid broadcasterId, DateTimeOffset now, DateTimeOffset until);

    bool TryGetCoolingUntil(Guid broadcasterId, DateTimeOffset now, out DateTimeOffset until);

    bool TryGetCooldown(Guid broadcasterId, DateTimeOffset now, out SpotifyCooldown cooldown);
}

/// <summary>One channel's cooldown: when Spotify refused it, and when it allows the next call.</summary>
public sealed record SpotifyCooldown(DateTimeOffset Since, DateTimeOffset Until)
{
    public TimeSpan Length => Until - Since;
}

/// <remarks>
/// A cooldown starting or ending changes the channel's action-required inbox
/// (<see cref="Notifications.Sources.SpotifyAppBlockedSource"/>), so both tell its dashboards.
/// </remarks>
public sealed class SpotifyRateLimitCooldowns(IActionRequiredChangeNotifier inbox)
    : ISpotifyRateLimitCooldowns
{
    private readonly ConcurrentDictionary<Guid, SpotifyCooldown> _cooldowns = new();

    public void CoolUntil(Guid broadcasterId, DateTimeOffset now, DateTimeOffset until)
    {
        _cooldowns[broadcasterId] = new(now, until);
        inbox.NotifyChanged(broadcasterId);
    }

    public bool TryGetCoolingUntil(Guid broadcasterId, DateTimeOffset now, out DateTimeOffset until)
    {
        bool cooling = TryGetCooldown(broadcasterId, now, out SpotifyCooldown cooldown);
        until = cooling ? cooldown.Until : default;
        return cooling;
    }

    public bool TryGetCooldown(Guid broadcasterId, DateTimeOffset now, out SpotifyCooldown cooldown)
    {
        if (!_cooldowns.TryGetValue(broadcasterId, out SpotifyCooldown? found))
        {
            cooldown = null!;
            return false;
        }

        if (found.Until > now)
        {
            cooldown = found;
            return true;
        }

        // Removes only the expired value, so a cooldown recorded in between is kept.
        if (_cooldowns.TryRemove(new KeyValuePair<Guid, SpotifyCooldown>(broadcasterId, found)))
            inbox.NotifyChanged(broadcasterId);
        cooldown = null!;
        return false;
    }
}
