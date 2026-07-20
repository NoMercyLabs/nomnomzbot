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
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform;

/// <summary>
/// Singleton in-memory registry of all active channel contexts.
/// Implements <see cref="IHostedService"/> to manage the background eviction timer.
/// </summary>
public sealed class ChannelRegistry : IChannelRegistry, IHostedService
{
    private readonly ConcurrentDictionary<Guid, ChannelContext> _channels = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChannelRegistry> _logger;
    private readonly TimeProvider _timeProvider;
    private Timer? _evictionTimer;

    // Eviction: remove channels that are offline AND have had no activity for 2 hours
    // Checked every 15 minutes
    private static readonly TimeSpan EvictionThreshold = TimeSpan.FromHours(2);
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(15);

    public ChannelRegistry(
        IServiceScopeFactory scopeFactory,
        ILogger<ChannelRegistry> logger,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // -------------------------------------------------------------------------
    // IHostedService
    // -------------------------------------------------------------------------

    public Task StartAsync(CancellationToken ct)
    {
        _evictionTimer = new(RunEviction, null, EvictionInterval, EvictionInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _evictionTimer?.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // IChannelRegistry
    // -------------------------------------------------------------------------

    public int Count => _channels.Count;

    public async Task<ChannelContext> GetOrCreateAsync(
        Guid broadcasterId,
        string twitchChannelId,
        string channelName,
        CancellationToken ct = default
    )
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (_channels.TryGetValue(broadcasterId, out ChannelContext? existing))
        {
            existing.LastActivityAt = now;
            return existing;
        }

        ChannelContext ctx = new()
        {
            BroadcasterId = broadcasterId,
            TwitchChannelId = twitchChannelId,
            ChannelName = channelName,
            LoadedAt = now,
            LastActivityAt = now,
        };

        // Load commands + builtin toggles + channel-wide settings from DB
        await LoadCommandsAsync(ctx, ct);
        await LoadBuiltinTogglesAsync(ctx, ct);
        await LoadChannelSettingsAsync(ctx, ct);
        await LoadChatTriggersAsync(ctx, ct);
        await LoadSoundTriggersAsync(ctx, ct);
        await LoadActiveChatPollAsync(ctx, ct);
        await LoadModerationStandingsAsync(ctx, ct);

        _channels[broadcasterId] = ctx;
        _logger.LogInformation(
            "Registered channel {BroadcasterId} ({ChannelName})",
            broadcasterId,
            channelName
        );
        return ctx;
    }

    public ChannelContext? Get(Guid broadcasterId) =>
        _channels.TryGetValue(broadcasterId, out ChannelContext? ctx) ? ctx : null;

    public async Task InvalidateCommandsAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        ctx.Commands.Clear();
        await LoadCommandsAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded {Count} commands for channel {BroadcasterId}",
            ctx.Commands.Count,
            broadcasterId
        );
    }

    public async Task InvalidateBuiltinsAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        ctx.DisabledBuiltins.Clear();
        ctx.BuiltinResponseOverrides.Clear();
        await LoadBuiltinTogglesAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded {Count} disabled builtin(s) for channel {BroadcasterId}",
            ctx.DisabledBuiltins.Count,
            broadcasterId
        );
    }

    public async Task InvalidateChatTriggersAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        ctx.ChatTriggers.Clear();
        await LoadChatTriggersAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded {Count} chat trigger(s) for channel {BroadcasterId}",
            ctx.ChatTriggers.Count,
            broadcasterId
        );
    }

    public async Task InvalidateModerationStandingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        ctx.ModerationStandings.Clear();
        await LoadModerationStandingsAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded {Count} moderation standing(s) for channel {BroadcasterId}",
            ctx.ModerationStandings.Count,
            broadcasterId
        );
    }

    public async Task InvalidateSoundTriggersAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        ctx.SoundTriggers.Clear();
        await LoadSoundTriggersAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded {Count} sound trigger(s) for channel {BroadcasterId}",
            ctx.SoundTriggers.Count,
            broadcasterId
        );
    }

    public async Task InvalidateSettingsAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(broadcasterId, out ChannelContext? ctx))
            return;

        await LoadChannelSettingsAsync(ctx, ct);

        _logger.LogDebug(
            "Reloaded channel settings (personality={Personality}) for channel {BroadcasterId}",
            ctx.Personality,
            broadcasterId
        );
    }

    public async Task RemoveAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        if (!_channels.TryRemove(broadcasterId, out ChannelContext? ctx))
            return;

        // Cancel all active pipelines before releasing the context
        foreach ((string executionId, CancellationTokenSource cts) in ctx.ActivePipelines)
        {
            try
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error cancelling pipeline {ExecutionId} for channel {BroadcasterId}",
                    executionId,
                    broadcasterId
                );
            }
        }

        ctx.ActivePipelines.Clear();
        _logger.LogInformation("Unregistered channel {BroadcasterId}", broadcasterId);
    }

    public IReadOnlyCollection<ChannelContext> GetAll() => _channels.Values.ToList().AsReadOnly();

    public IReadOnlyCollection<ChannelContext> GetLiveChannels() =>
        _channels.Values.Where(c => c.IsLive).ToList().AsReadOnly();

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task LoadModerationStandingsAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<Domain.Moderation.Entities.ChannelModerationStanding> standings = await db
            .ChannelModerationStandings.Where(s => s.BroadcasterId == ctx.BroadcasterId)
            .ToListAsync(ct);
        foreach (Domain.Moderation.Entities.ChannelModerationStanding standing in standings)
            ctx.ModerationStandings[$"{standing.Provider}:{standing.UserId}"] = standing.Standing;
    }

    private async Task LoadActiveChatPollAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Domain.Community.Entities.ChatPoll? open = await db.ChatPolls.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == ctx.BroadcasterId
                && p.Status == Domain.Community.Entities.ChatPollStatus.Open,
            ct
        );
        ctx.ActiveChatPoll = open is null
            ? null
            : new CachedChatPoll { Id = open.Id, OptionCount = CountPollOptions(open.OptionsJson) };
    }

    /// <summary>Options are a JSON string array; a malformed payload yields 0 (the poll can't be voted).</summary>
    private static int CountPollOptions(string optionsJson)
    {
        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(
                optionsJson
            );
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    private async Task LoadSoundTriggersAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Only enabled clips that actually carry a trigger word enter the hot-path cache.
        var clips = await db
            .SoundClips.Where(c =>
                c.BroadcasterId == ctx.BroadcasterId && c.IsEnabled && c.TriggerWord != null
            )
            .Select(c => new
            {
                c.Id,
                c.TriggerWord,
                c.CooldownSeconds,
                c.MinPermissionLevel,
            })
            .ToListAsync(ct);

        foreach (var clip in clips)
        {
            // TriggerWord is stored already lower-cased; the dictionary's OrdinalIgnoreCase comparer makes the
            // lookup case-insensitive against whatever case the chatter typed.
            ctx.SoundTriggers[clip.TriggerWord!] = new CachedSoundTrigger
            {
                ClipId = clip.Id,
                TriggerWord = clip.TriggerWord!,
                CooldownSeconds = clip.CooldownSeconds,
                MinPermissionLevel = clip.MinPermissionLevel,
            };
        }
    }

    private async Task LoadChatTriggersAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<Domain.Commands.Entities.ChatTrigger> triggers = await db
            .ChatTriggers.Include(t => t.Pipeline)
            .Where(t => t.BroadcasterId == ctx.BroadcasterId && t.IsEnabled)
            .ToListAsync(ct);

        foreach (Domain.Commands.Entities.ChatTrigger trigger in triggers)
        {
            System.Text.RegularExpressions.Regex? compiled = null;
            if (trigger.MatchType == Domain.Commands.Entities.ChatTriggerMatchType.Regex)
            {
                try
                {
                    // Compiled once per cache load; the hard timeout bounds any pathological pattern
                    // to milliseconds per message instead of a hot-path hang.
                    compiled = new System.Text.RegularExpressions.Regex(
                        trigger.Pattern,
                        trigger.CaseSensitive
                            ? System.Text.RegularExpressions.RegexOptions.None
                            : System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(100)
                    );
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Skipping chat trigger {TriggerId} with invalid regex for {BroadcasterId}",
                        trigger.Id,
                        ctx.BroadcasterId
                    );
                    continue;
                }
            }

            ctx.ChatTriggers[trigger.Id] = new CachedChatTrigger
            {
                Id = trigger.Id,
                Pattern = trigger.Pattern,
                MatchType = trigger.MatchType,
                CaseSensitive = trigger.CaseSensitive,
                Response = trigger.Response,
                PipelineGraphJson = trigger.Pipeline?.GraphJsonCache,
                CooldownSeconds = trigger.CooldownSeconds,
                MinPermissionLevel = trigger.MinPermissionLevel,
                CompiledRegex = compiled,
            };
        }
    }

    private async Task LoadCommandsAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Anonymous projection (the one place house style allows `var`) carries BOTH response fields out of SQL;
        // the array is then normalised in memory because EF cannot build a conditional array in SQL.
        var rows = await db
            .Commands.Where(c =>
                c.BroadcasterId == ctx.BroadcasterId && c.IsEnabled && c.DeletedAt == null
            )
            .Select(c => new
            {
                c.Name,
                c.TemplateResponses,
                c.TemplateResponse,
                c.CooldownPerUser,
                c.CooldownSeconds,
                c.MinPermissionLevel,
                c.Tier,
                PipelineGraphJson = c.Pipeline != null ? c.Pipeline.GraphJsonCache : null,
                c.Aliases,
            })
            .ToListAsync(ct);

        List<CachedCommand> commands = rows.Select(c => new CachedCommand
            {
                Name = c.Name,
                TemplateResponses = NormalizeResponses(c.TemplateResponses, c.TemplateResponse),
                GlobalCooldown = c.CooldownPerUser ? 0 : c.CooldownSeconds,
                UserCooldown = c.CooldownPerUser ? c.CooldownSeconds : 0,
                MinPermissionLevel = c.MinPermissionLevel,
                Tier = c.Tier,
                PipelineGraphJson = c.PipelineGraphJson,
                Aliases = c.Aliases.ToArray(),
            })
            .ToList();

        foreach (CachedCommand cmd in commands)
        {
            // ChatMessageHandler parses the trigger by stripping the leading '!' and lowercasing, so registry
            // keys must match that form — "sr" not "!sr". Strip here so any command stored with or without the
            // prefix resolves correctly from either direction.
            ctx.Commands[cmd.Name.TrimStart('!').ToLowerInvariant()] = cmd;
            foreach (string alias in cmd.Aliases)
                ctx.Commands[alias.TrimStart('!').ToLowerInvariant()] = cmd;
        }

        _logger.LogDebug(
            "Loaded {Count} commands for channel {BroadcasterId}",
            commands.Count,
            ctx.BroadcasterId
        );
    }

    // A command authored as a single response persists it in the singular TemplateResponse (the plural array stays
    // empty); promote it into the array the chat path reads so a single-response command — e.g. one whose only
    // response carries a {list.pick.<name>} token — actually fires in chat instead of resolving to an empty message.
    // Mirrors the API execute path's singular fallback. Pure so it is unit-tested without a database.
    internal static string[] NormalizeResponses(
        IReadOnlyList<string>? responses,
        string? singular
    ) =>
        responses is { Count: > 0 } ? responses.ToArray()
        : string.IsNullOrEmpty(singular) ? []
        : [singular];

    private async Task LoadBuiltinTogglesAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // One pass over the channel's builtin rows: disabled keys → DisabledBuiltins; any response-template
        // override (OverridesJson) → BuiltinResponseOverrides. Anonymous projection forces `var`.
        var rows = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == ctx.BroadcasterId)
            .Select(c => new
            {
                c.BuiltinKey,
                c.IsEnabled,
                c.OverridesJson,
            })
            .ToListAsync(ct);

        int disabledCount = 0;
        foreach (var row in rows)
        {
            // Normalizes away the leading "!" some rows carry (DefaultCommandsSeeder writes bang-prefixed
            // keys) so the lookup always matches ChatMessageHandler's bare, lowercased parsed command name.
            string key = row.BuiltinKey.TrimStart('!').ToLowerInvariant();

            if (!row.IsEnabled)
            {
                ctx.DisabledBuiltins[key] = 0;
                disabledCount++;
            }

            if (TryParseResponseTemplateOverride(row.OverridesJson, out string? template))
                ctx.BuiltinResponseOverrides[key] = template;
        }

        _logger.LogDebug(
            "Loaded {DisabledCount} disabled builtin(s) and {OverrideCount} response override(s) for channel {BroadcasterId}",
            disabledCount,
            ctx.BuiltinResponseOverrides.Count,
            ctx.BroadcasterId
        );
    }

    private async Task LoadChannelSettingsAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var settings = await db
            .Channels.Where(c => c.Id == ctx.BroadcasterId)
            .Select(c => new { c.Personality, c.CommandPrefix })
            .FirstOrDefaultAsync(ct);

        ctx.Personality = PersonalityTone.Normalize(settings?.Personality);
        // Guard against an empty/whitespace stored prefix (never expected past validation) so the hot path's
        // StartsWith check can never match every message.
        ctx.CommandPrefix = string.IsNullOrWhiteSpace(settings?.CommandPrefix)
            ? "!"
            : settings.CommandPrefix;
    }

    /// <summary>
    /// Extracts a built-in's response-template override from its <c>OverridesJson</c> — schema
    /// <c>{ "responseTemplate": "..." }</c>. Returns false for null/blank/malformed JSON or an empty template,
    /// so a broken override never crashes the registry load and simply leaves the built-in on its tone default.
    /// </summary>
    private static bool TryParseResponseTemplateOverride(string? overridesJson, out string template)
    {
        template = string.Empty;
        if (string.IsNullOrWhiteSpace(overridesJson))
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(overridesJson);
            if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("responseTemplate", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
            )
            {
                string? parsed = value.GetString();
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    template = parsed;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed override JSON — ignore; the built-in keeps its tone default.
        }

        return false;
    }

    private void RunEviction(object? state)
    {
        DateTimeOffset threshold = _timeProvider.GetUtcNow() - EvictionThreshold;
        List<ChannelContext> candidates = _channels
            .Values.Where(c => !c.IsLive && c.LastActivityAt < threshold)
            .ToList();

        foreach (ChannelContext ctx in candidates)
        {
            if (_channels.TryRemove(ctx.BroadcasterId, out _))
                _logger.LogInformation("Evicted idle channel {BroadcasterId}", ctx.BroadcasterId);
        }
    }
}
