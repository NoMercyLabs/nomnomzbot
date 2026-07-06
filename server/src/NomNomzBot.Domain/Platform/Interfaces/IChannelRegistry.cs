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
    // broadcasterId is the tenant (channel) Guid; twitchChannelId is the Twitch string id used for
    // Helix/IRC; channelName is the channel login name.
    Task<ChannelContext> GetOrCreateAsync(
        Guid broadcasterId,
        string twitchChannelId,
        string channelName,
        CancellationToken ct = default
    );
    ChannelContext? Get(Guid broadcasterId);

    /// <summary>
    /// Reloads the command cache for an already-registered channel.
    /// Call after any Create/Update/Delete/Toggle on a command so the
    /// in-process handler picks up the change without a restart.
    /// No-ops if the channel is not yet in the registry.
    /// </summary>
    Task InvalidateCommandsAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Reloads the built-in command toggle cache for an already-registered channel. Call after a builtin's
    /// per-channel enable/disable state changes so the in-process chat handler picks up the change without a
    /// restart. No-ops if the channel is not yet in the registry.
    /// </summary>
    Task InvalidateBuiltinsAsync(Guid broadcasterId, CancellationToken ct = default);

    Task RemoveAsync(Guid broadcasterId, CancellationToken ct = default);
    IReadOnlyCollection<ChannelContext> GetAll();
    IReadOnlyCollection<ChannelContext> GetLiveChannels();
    int Count { get; }
}

/// <summary>
/// Full in-memory state object for a channel the bot is connected to.
/// </summary>
public class ChannelContext
{
    // Tenant (channel) Guid — the registry key. The Twitch string id is carried separately for transport.
    public required Guid BroadcasterId { get; init; }
    public required string TwitchChannelId { get; init; }
    public required string ChannelName { get; init; }
    public string? DisplayName { get; set; }
    public bool IsLive { get; set; }
    public string? CurrentStreamId { get; set; }
    public string? CurrentTitle { get; set; }
    public string? CurrentGame { get; set; }
    public DateTimeOffset? WentLiveAt { get; set; }

    // Live concurrent viewer count from Helix Get Streams, kept fresh by StreamStatusPollingService (0 when offline).
    // The dashboard reads this so a live channel's viewer count is present from startup, without a per-request Helix call.
    public int ViewerCount { get; set; }

    // Per-channel in-memory command cache: key = command name (lowercase)
    public ConcurrentDictionary<string, CachedCommand> Commands { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-channel builtin-toggle cache: keys are the builtin's bare catalog key (lowercase, no leading "!"
    /// — the same form the builtin catalog and <c>ChatMessageHandler</c>'s parsed command name use) for every
    /// builtin explicitly disabled for this channel. Absence = enabled (the catalog default), mirroring
    /// <c>IBuiltinCommandService</c>'s "absent row = enabled" semantics.
    /// </summary>
    public ConcurrentDictionary<string, byte> DisabledBuiltins { get; } =
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

    // Stamped by ChannelRegistry via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — this context object does not self-stamp time.
    public DateTimeOffset LoadedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }

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

    /// <summary>Template responses (round-robin). Empty for pipeline/code tier commands.</summary>
    public required string[] TemplateResponses { get; init; }

    public required int GlobalCooldown { get; init; }
    public required int UserCooldown { get; init; }

    /// <summary>Minimum permission level integer (0=everyone, 4=moderator, 5=broadcaster).</summary>
    public required int MinPermissionLevel { get; init; }

    /// <summary>template | pipeline | code</summary>
    public required string Tier { get; init; }

    /// <summary>
    /// Resolved pipeline graph-cache JSON — from the bound Pipeline entity.
    /// Null for template-tier commands.
    /// </summary>
    public string? PipelineGraphJson { get; init; }

    public string[] Aliases { get; init; } = [];
}
