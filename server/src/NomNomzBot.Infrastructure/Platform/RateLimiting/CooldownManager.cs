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
using NomNomzBot.Application.Abstractions.RateLimiting;

namespace NomNomzBot.Infrastructure.Platform.RateLimiting;

/// <summary>
/// ICooldownManager implementation using ConcurrentDictionary.
/// Tracks cooldowns for commands per-channel and optionally per-user.
/// Thread-safe for concurrent access from multiple event handlers.
/// </summary>
public sealed class CooldownManager(TimeProvider timeProvider) : ICooldownManager
{
    // Key format: "{broadcasterId}:{commandName}" for global cooldowns
    //             "{broadcasterId}:{commandName}:{userId}" for per-user cooldowns
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldowns = new();

    public bool IsOnCooldown(string channelId, string commandName, string? userId = null)
    {
        string key = BuildKey(channelId, commandName, userId);

        if (!_cooldowns.TryGetValue(key, out DateTimeOffset expiresAt))
        {
            return false;
        }

        if (timeProvider.GetUtcNow() >= expiresAt)
        {
            // Cooldown expired, clean up
            _cooldowns.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    public TimeSpan? GetRemainingCooldown(
        string channelId,
        string commandName,
        string? userId = null
    )
    {
        string key = BuildKey(channelId, commandName, userId);

        if (!_cooldowns.TryGetValue(key, out DateTimeOffset expiresAt))
        {
            return null;
        }

        TimeSpan remaining = expiresAt - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            _cooldowns.TryRemove(key, out _);
            return null;
        }

        return remaining;
    }

    public void SetCooldown(
        string channelId,
        string commandName,
        TimeSpan duration,
        string? userId = null
    )
    {
        string key = BuildKey(channelId, commandName, userId);
        DateTimeOffset expiresAt = timeProvider.GetUtcNow().Add(duration);
        _cooldowns[key] = expiresAt;
    }

    public void ClearCooldown(string channelId, string commandName, string? userId = null)
    {
        string key = BuildKey(channelId, commandName, userId);
        _cooldowns.TryRemove(key, out _);
    }

    public void ClearAllCooldowns(string channelId)
    {
        string prefix = $"{channelId}:";
        IEnumerable<string> keysToRemove = _cooldowns.Keys.Where(k =>
            k.StartsWith(prefix, StringComparison.Ordinal)
        );

        foreach (string key in keysToRemove)
        {
            _cooldowns.TryRemove(key, out _);
        }
    }

    private static string BuildKey(string channelId, string commandName, string? userId)
    {
        return userId is null
            ? $"{channelId}:{commandName}"
            : $"{channelId}:{commandName}:{userId}";
    }
}
