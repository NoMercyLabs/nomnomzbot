// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The general missing-scope mechanism (identity-auth §3.4a). One service owns the full lifecycle of "a feature
/// needs a scope the streamer token doesn't have": reactive detection (record a real Helix <c>missing_scope</c>
/// failure), the dashboard read model (proactive feature-gated gaps ∪ reactive runtime gaps), the idempotent
/// one-time chat notice, and clearing a gap once a re-grant restores the scope. It derives required scopes from
/// the offered-feature → scope registry (<see cref="FeatureScopeMap"/>), so it covers any scope/any feature and
/// can never disagree with the grant-aware enable flow that reads the same registry.
/// </summary>
public sealed class ScopeNotificationService : IScopeNotificationService
{
    private readonly IApplicationDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScopeNotificationService> _logger;

    // The chat provider AND the bot-readiness gate are resolved lazily (only NotifyPendingAsync uses them) to
    // break the construction cycle: this service is reached from ScopeGrantService (via the token vault's scope
    // reconciliation), and both IChatProvider and IPlatformBotReadinessGate transitively depend back on
    // ITwitchTokenResolver → … → IScopeGrantService. Resolving on demand from the same scoped provider keeps one
    // DbContext + token scope per call without closing that loop at registration time.
    public ScopeNotificationService(
        IApplicationDbContext db,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<ScopeNotificationService> logger
    )
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<bool>> RecordMissingScopeAsync(
        Guid broadcasterId,
        string scope,
        string? feature,
        CancellationToken ct = default
    )
    {
        if (broadcasterId == Guid.Empty || string.IsNullOrWhiteSpace(scope))
            return Result.Success(false);

        IntegrationConnection? connection = await TwitchConnectionAsync(broadcasterId, ct);
        if (connection is null)
            // No Twitch connection to attribute the gap to (app/bot-token call) — nothing to surface here.
            return Result.Success(false);

        // A stale failure for a scope the connection actually holds must not get stuck as a recorded gap.
        HashSet<string> granted = new(connection.Scopes, StringComparer.OrdinalIgnoreCase);
        if (granted.Contains(scope))
            return Result.Success(false);

        ChannelMissingScope? existing = await _db.ChannelMissingScopes.FirstOrDefaultAsync(
            m => m.BroadcasterId == broadcasterId && m.Scope == scope,
            ct
        );
        if (existing is not null)
        {
            // Already recorded — keep the first-known feature attribution but never duplicate or re-notify.
            if (existing.Feature is null && feature is not null)
            {
                existing.Feature = feature;
                await _db.SaveChangesAsync(ct);
            }
            return Result.Success(false);
        }

        _db.ChannelMissingScopes.Add(
            new ChannelMissingScope
            {
                BroadcasterId = broadcasterId,
                Scope = scope,
                Feature = feature ?? FeatureScopeMap.FeatureForScope(scope),
                DetectedAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded missing Twitch scope '{Scope}' for channel {BroadcasterId} (blocks feature '{Feature}')",
            scope,
            broadcasterId,
            feature ?? FeatureScopeMap.FeatureForScope(scope) ?? "unknown"
        );
        return Result.Success(true);
    }

    public async Task<Result<MissingScopesDto>> GetMissingScopesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await TwitchConnectionAsync(broadcasterId, ct);
        if (connection is null)
            return Result.Failure<MissingScopesDto>(
                "This channel has no Twitch connection.",
                "NOT_FOUND"
            );

        HashSet<string> granted = new(connection.Scopes, StringComparer.OrdinalIgnoreCase);

        // Which scopes were detected missing at runtime (and whether each was announced) — keyed for lookup.
        Dictionary<string, ChannelMissingScope> runtime = await _db
            .ChannelMissingScopes.Where(m => m.BroadcasterId == broadcasterId)
            .ToDictionaryAsync(m => m.Scope, StringComparer.OrdinalIgnoreCase, ct);

        // The proactive truth: every offered feature's required scope the connection does not hold. Grouped per
        // scope with the feature(s) it blocks, so one scope that gates several features renders as one row.
        Dictionary<string, SortedSet<string>> featuresByScope = new(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (KeyValuePair<string, IReadOnlyList<string>> feature in FeatureScopeMap.Features)
        foreach (string scope in feature.Value)
        {
            if (granted.Contains(scope))
                continue;
            if (!featuresByScope.TryGetValue(scope, out SortedSet<string>? features))
            {
                features = new SortedSet<string>(StringComparer.Ordinal);
                featuresByScope[scope] = features;
            }
            features.Add(feature.Key);
        }

        // A runtime-detected scope that is not in any offered-feature map still surfaces (a raw Helix gap).
        foreach (string scope in runtime.Keys)
            if (!granted.Contains(scope) && !featuresByScope.ContainsKey(scope))
                featuresByScope[scope] = new SortedSet<string>(StringComparer.Ordinal);

        List<MissingScopeDto> rows =
        [
            .. featuresByScope
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new MissingScopeDto(
                    Scope: kv.Key,
                    Features: [.. kv.Value],
                    DetectedAtRuntime: runtime.ContainsKey(kv.Key),
                    ChatNotified: runtime.TryGetValue(kv.Key, out ChannelMissingScope? m)
                        && m.ChatNotifiedAt is not null
                )),
        ];

        return Result.Success(new MissingScopesDto(connection.Status, rows));
    }

    public async Task<Result<int>> NotifyPendingAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<ChannelMissingScope> pending = await _db
            .ChannelMissingScopes.Where(m =>
                m.BroadcasterId == broadcasterId && m.ChatNotifiedAt == null
            )
            .OrderBy(m => m.DetectedAt)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return Result.Success(0);

        // Don't burn the one-shot notice on a send that will silently fail: only post when the bot can actually
        // talk to Twitch. If it can't, the gap stays un-notified for a later retry — the dashboard still shows it.
        IPlatformBotReadinessGate botReadiness =
            _serviceProvider.GetRequiredService<IPlatformBotReadinessGate>();
        if (!await botReadiness.IsPlatformBotConfiguredAsync(ct))
        {
            _logger.LogDebug(
                "Deferring {Count} missing-scope chat notice(s) for {BroadcasterId} — the bot is not connected",
                pending.Count,
                broadcasterId
            );
            return Result.Success(0);
        }

        IChatProvider chat = _serviceProvider.GetRequiredService<IChatProvider>();
        int announced = 0;
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (ChannelMissingScope gap in pending)
        {
            await chat.SendMessageAsync(broadcasterId, BuildNotice(gap), ct);
            gap.ChatNotifiedAt = now;
            announced++;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(announced);
    }

    public async Task<Result<IReadOnlyList<string>>> ClearResolvedAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> grantedScopes,
        CancellationToken ct = default
    )
    {
        if (broadcasterId == Guid.Empty)
            return Result.Success<IReadOnlyList<string>>([]);

        HashSet<string> granted = new(grantedScopes, StringComparer.OrdinalIgnoreCase);

        List<ChannelMissingScope> recorded = await _db
            .ChannelMissingScopes.Where(m => m.BroadcasterId == broadcasterId)
            .ToListAsync(ct);

        List<ChannelMissingScope> resolved = [.. recorded.Where(m => granted.Contains(m.Scope))];
        if (resolved.Count == 0)
            return Result.Success<IReadOnlyList<string>>([]);

        _db.ChannelMissingScopes.RemoveRange(resolved);
        await _db.SaveChangesAsync(ct);

        IReadOnlyList<string> clearedScopes = [.. resolved.Select(m => m.Scope)];
        _logger.LogInformation(
            "Cleared {Count} resolved missing-scope gap(s) for channel {BroadcasterId}: {Scopes}",
            clearedScopes.Count,
            broadcasterId,
            string.Join(", ", clearedScopes)
        );
        return Result.Success(clearedScopes);
    }

    public async Task<Result<IReadOnlyList<string>>> BuildRegrantScopeSetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<MissingScopesDto> missing = await GetMissingScopesAsync(broadcasterId, ct);
        if (missing.IsFailure)
            return missing.WithValue<IReadOnlyList<string>>(null!);

        if (missing.Value.Scopes.Count == 0)
            return Result.Failure<IReadOnlyList<string>>(
                "No scopes are missing — nothing to grant.",
                "NO_MISSING_SCOPES"
            );

        IntegrationConnection? connection = await TwitchConnectionAsync(broadcasterId, ct);
        if (connection is null)
            return Result.Failure<IReadOnlyList<string>>(
                "This channel has no Twitch connection.",
                "NOT_FOUND"
            );

        // The additive union: keep every currently-granted scope (silent re-consent) and add the missing ones, so
        // the operator never loses a permission by re-granting. Deduplicated case-insensitively, stably ordered.
        SortedSet<string> union = new(connection.Scopes, StringComparer.OrdinalIgnoreCase);
        foreach (MissingScopeDto row in missing.Value.Scopes)
            union.Add(row.Scope);

        return Result.Success<IReadOnlyList<string>>([.. union]);
    }

    /// <summary>The streamer-token Twitch connection for the channel, or null when none exists.</summary>
    private async Task<IntegrationConnection?> TwitchConnectionAsync(
        Guid broadcasterId,
        CancellationToken ct
    ) =>
        await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == AuthEnums.IntegrationProvider.Twitch
                    && c.DeletedAt == null,
                ct
            );

    /// <summary>The single, friendly chat line that tells the streamer which permission the bot needs and why.</summary>
    private static string BuildNotice(ChannelMissingScope gap)
    {
        string? feature = gap.Feature ?? FeatureScopeMap.FeatureForScope(gap.Scope);
        string purpose = feature is null
            ? "use a feature you have on"
            : FeatureScopeMap.DescribeFeature(feature);
        return $"I need the '{gap.Scope}' Twitch permission to {purpose} — grant it from your NomNomzBot dashboard and I'll pick it up automatically.";
    }
}
