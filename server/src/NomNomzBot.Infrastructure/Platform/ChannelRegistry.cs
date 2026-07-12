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

    private async Task LoadCommandsAsync(ChannelContext ctx, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<CachedCommand> commands = await db
            .Commands.Where(c =>
                c.BroadcasterId == ctx.BroadcasterId && c.IsEnabled && c.DeletedAt == null
            )
            .Select(c => new CachedCommand
            {
                Name = c.Name,
                TemplateResponses = (c.TemplateResponses ?? new List<string>()).ToArray(),
                GlobalCooldown = c.CooldownPerUser ? 0 : c.CooldownSeconds,
                UserCooldown = c.CooldownPerUser ? c.CooldownSeconds : 0,
                MinPermissionLevel = c.MinPermissionLevel,
                Tier = c.Tier,
                PipelineGraphJson = c.Pipeline != null ? c.Pipeline.GraphJsonCache : null,
                Aliases = c.Aliases.ToArray(),
            })
            .ToListAsync(ct);

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

        string? personality = await db
            .Channels.Where(c => c.Id == ctx.BroadcasterId)
            .Select(c => c.Personality)
            .FirstOrDefaultAsync(ct);

        ctx.Personality = PersonalityTone.Normalize(personality);
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
