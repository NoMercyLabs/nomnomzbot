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
using NomNomzBot.Domain.Identity.Enums;

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

    /// <summary>
    /// Reloads the cached channel-wide settings (currently the personality tone) for an already-registered
    /// channel. Call after a settings change so the in-process chat handler picks it up without a restart.
    /// No-ops if the channel is not yet in the registry.
    /// </summary>
    Task InvalidateSettingsAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Reloads the keyword chat-trigger cache for an already-registered channel. Call after any
    /// Create/Update/Delete/Toggle on a chat trigger so the in-process chat handler picks up the change
    /// without a restart. No-ops if the channel is not yet in the registry.
    /// </summary>
    Task InvalidateChatTriggersAsync(Guid broadcasterId, CancellationToken ct = default);

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

    /// <summary>
    /// The channel's built-in-command personality tone (<see cref="PersonalityTone"/>), loaded from
    /// <c>Channel.Personality</c>. Read on the chat hot path to phrase built-in responses; defaults to
    /// <see cref="PersonalityTone.Informative"/> until the registry loads the channel's setting.
    /// </summary>
    public string Personality { get; set; } = PersonalityTone.Informative;

    /// <summary>
    /// Per-channel built-in response-template overrides: key = the built-in's bare catalog key (lowercase,
    /// no leading "!"), value = the override template parsed from <c>ChannelBuiltinCommand.OverridesJson</c>.
    /// Absence = no override (fall back to the tone template). Populated by <c>ChannelRegistry</c> alongside
    /// the builtin toggles.
    /// </summary>
    public ConcurrentDictionary<string, string> BuiltinResponseOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

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
    /// Per-channel keyword chat-trigger cache (enabled triggers only), keyed by trigger id — matched
    /// against every ordinary (non-command) chat line on the hot path. Populated by <c>ChannelRegistry</c>;
    /// refresh via <see cref="IChannelRegistry.InvalidateChatTriggersAsync"/>.
    /// </summary>
    public ConcurrentDictionary<Guid, CachedChatTrigger> ChatTriggers { get; } = new();

    /// <summary>
    /// The channel's OPEN bot-run chat poll, or null — the hot path checks this before treating a bare
    /// number as a vote. Set by the poll service on open/close and loaded at registration.
    /// </summary>
    public CachedChatPoll? ActiveChatPoll { get; set; }

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

/// <summary>The open chat poll's hot-path shape: enough to turn "2" into a vote without a DB read.</summary>
public class CachedChatPoll
{
    public required Guid Id { get; init; }
    public required int OptionCount { get; init; }
}

/// <summary>
/// Cached representation of a keyword chat trigger, hot-path ready: for <c>regex</c> triggers the
/// pattern is pre-compiled ONCE at load with a hard match timeout (a malicious pattern can burn at most
/// that long per message); an invalid regex never enters the cache.
/// </summary>
public class CachedChatTrigger
{
    public required Guid Id { get; init; }
    public required string Pattern { get; init; }

    /// <summary>contains | exact | starts_with | regex.</summary>
    public required string MatchType { get; init; }

    public required bool CaseSensitive { get; init; }
    public string? Response { get; init; }
    public string? PipelineGraphJson { get; init; }
    public required int CooldownSeconds { get; init; }
    public required int MinPermissionLevel { get; init; }

    /// <summary>Set only for <c>regex</c> triggers (compiled, bounded by a match timeout).</summary>
    public System.Text.RegularExpressions.Regex? CompiledRegex { get; init; }
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
