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

namespace NomNomzBot.Domain.Platform.Interfaces;

/// <summary>
/// Registry for active channels the bot is connected to.
/// Provides fast in-memory access to channel state without hitting the database.
/// </summary>
public interface IChannelRegistry
{
    Task<ChannelContext> GetOrCreateAsync(
        string broadcasterId,
        string channelName,
        CancellationToken ct = default
    );
    ChannelContext? Get(string broadcasterId);
    Task RemoveAsync(string broadcasterId, CancellationToken ct = default);
    IReadOnlyCollection<ChannelContext> GetAll();
    IReadOnlyCollection<ChannelContext> GetLiveChannels();
    int Count { get; }
}

/// <summary>
/// Full in-memory state object for a channel the bot is connected to.
/// </summary>
public class ChannelContext
{
    public required string BroadcasterId { get; init; }
    public required string ChannelName { get; init; }
    public string? DisplayName { get; set; }
    public bool IsLive { get; set; }
    public string? CurrentStreamId { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentGame { get; set; }
    public DateTimeOffset? WentLiveAt { get; set; }

    // Per-channel in-memory command cache: key = command name (lowercase)
    public ConcurrentDictionary<string, CachedCommand> Commands { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Per-channel active pipelines: key = executionId
    public ConcurrentDictionary<string, CancellationTokenSource> ActivePipelines { get; } = new();

    // Cooldown tracking: key = "commandName:userId" or "commandName:global"
    public ConcurrentDictionary<string, DateTimeOffset> Cooldowns { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Track last shoutout sent to each user: key = userId, value = DateTimeOffset
    public ConcurrentDictionary<string, DateTimeOffset> LastShoutoutPerUser { get; } = new();
    public DateTimeOffset? LastGlobalShoutout { get; set; }

    // Session chatters seen since bot joined: key = userId, value = displayName
    public ConcurrentDictionary<string, string> SessionChatters { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Messages received since the bot joined. Used by TimerService for MinChatActivity checks.</summary>
    public long MessageCount { get; set; }

    /// <summary>Commands successfully executed since the bot joined this session.</summary>
    public long CommandsUsed { get; set; }

    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    // Lock for compound operations
    private readonly object _lock = new();
    public object Lock => _lock;
}

/// <summary>
/// Cached representation of a command loaded from the database.
/// </summary>
public class CachedCommand
{
    public required string Name { get; init; }
    public required string[] Responses { get; init; }
    public required int GlobalCooldown { get; init; }
    public required int UserCooldown { get; init; }
    public required string Permission { get; init; }
    public required string Type { get; init; }
    public string? PipelineJson { get; init; }
    public string[] Aliases { get; init; } = [];
}
