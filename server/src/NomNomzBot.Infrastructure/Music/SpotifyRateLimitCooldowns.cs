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
using NomNomzBot.Application.Abstractions.Caching;
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

    /// <summary>Loads a cooldown saved before a restart, once per channel per process.</summary>
    Task RestoreAsync(Guid broadcasterId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Counts one outbound Spotify call for this channel.</summary>
    void RecordCall(Guid broadcasterId, DateTimeOffset now, bool isBackgroundPoll);

    /// <summary>Calls this channel sent in the hour before <paramref name="now"/>.</summary>
    SpotifyCallCounts CallsInLastHour(Guid broadcasterId, DateTimeOffset now);
}

/// <summary>Outbound Spotify calls of one channel over the last hour, split by who asked.</summary>
public sealed record SpotifyCallCounts(int Background, int Interactive)
{
    public int Total => Background + Interactive;
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
public sealed class SpotifyRateLimitCooldowns(
    IActionRequiredChangeNotifier inbox,
    ICacheService? cache = null
) : ISpotifyRateLimitCooldowns
{
    private static readonly TimeSpan CallWindow = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, SpotifyCooldown> _cooldowns = new();
    private readonly ConcurrentDictionary<Guid, bool> _restored = new();
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentQueue<(DateTimeOffset At, bool Background)>
    > _calls = new();

    private static string CacheKey(Guid broadcasterId) => $"spotify:cooldown:{broadcasterId:N}";

    public void CoolUntil(Guid broadcasterId, DateTimeOffset now, DateTimeOffset until)
    {
        _cooldowns[broadcasterId] = new(now, until);
        inbox.NotifyChanged(broadcasterId);
        // Spotify's block outlives this process: without a saved copy a restart forgets it and the next
        // call walks straight into another 429.
        if (cache is not null)
            _ = cache.SetAsync(
                CacheKey(broadcasterId),
                new SpotifyCooldown(now, until),
                until - now
            );
    }

    public async Task RestoreAsync(
        Guid broadcasterId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (cache is null || !_restored.TryAdd(broadcasterId, true))
            return;
        SpotifyCooldown? saved = await cache.GetAsync<SpotifyCooldown>(
            CacheKey(broadcasterId),
            cancellationToken
        );
        if (saved is null || saved.Until <= now)
            return;
        if (_cooldowns.TryAdd(broadcasterId, saved))
            inbox.NotifyChanged(broadcasterId);
    }

    public void RecordCall(Guid broadcasterId, DateTimeOffset now, bool isBackgroundPoll) =>
        _calls.GetOrAdd(broadcasterId, _ => new()).Enqueue((now, isBackgroundPoll));

    public SpotifyCallCounts CallsInLastHour(Guid broadcasterId, DateTimeOffset now)
    {
        if (
            !_calls.TryGetValue(
                broadcasterId,
                out ConcurrentQueue<(DateTimeOffset At, bool Background)>? queue
            )
        )
            return new(0, 0);
        while (
            queue.TryPeek(out (DateTimeOffset At, bool Background) oldest)
            && now - oldest.At > CallWindow
        )
            queue.TryDequeue(out _);
        int background = queue.Count(c => c.Background);
        return new(background, queue.Count - background);
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
