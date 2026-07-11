// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Engagement.Dtos;
using NomNomzBot.Application.Engagement.Services;
using NomNomzBot.Domain.Engagement.Entities;
using NomNomzBot.Domain.Engagement.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Engagement;

/// <summary>
/// <see cref="IEngagementService"/> (engagement.md §3). Owns the per-viewer engagement state and the
/// detect→fire state machine; publishes at most one engagement event per message per the D1/D3/D4 rules.
/// Pure triggers — no greeting policy (D2); the bound pipeline/event-response does the greeting.
/// </summary>
public sealed class EngagementService : IEngagementService
{
    /// <summary>The per-channel greet burst limiter's cooldown bucket key (D4).</summary>
    private const string GreetCooldownKey = "engagement.greet";

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly ICooldownManager _cooldowns;

    public EngagementService(
        IApplicationDbContext db,
        IEventBus eventBus,
        ICooldownManager cooldowns
    )
    {
        _db = db;
        _eventBus = eventBus;
        _cooldowns = cooldowns;
    }

    public async Task<Result> OnChatActivityAsync(
        Guid broadcasterId,
        EngagementSignal signal,
        CancellationToken ct = default
    )
    {
        EngagementConfig? config = await _db.EngagementConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        // Default-deny: no config or every trigger off → nothing to do (the enabled-flags fast-path).
        if (config is null || !AnyEnabled(config))
            return Result.Success();

        ViewerEngagementState? state = await _db.ViewerEngagementStates.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.ViewerUserId == signal.ViewerUserId,
            ct
        );

        // The engagement event is captured as a typed publish closure, then invoked AFTER the state save
        // succeeds so a failed write never emits a phantom greeting. The closure preserves each event's
        // concrete compile-time type — the bus dispatches handlers by typeof(TEvent), not GetType().
        Func<CancellationToken, Task>? publish = null;

        if (state is null)
        {
            state = new ViewerEngagementState
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = signal.ViewerUserId,
                ViewerTwitchUserId = HashViewer(signal.ViewerExternalUserId),
                FirstChatAt = signal.At,
                LastChatAt = signal.At,
                LastSeenStreamSessionId = signal.CurrentStreamSessionId,
                ConsecutiveStreams = 1,
            };
            await _db.ViewerEngagementStates.AddAsync(state, ct);

            if (config.FirstTimeChatterEnabled && TryClaimGreet(broadcasterId, config))
            {
                state.LastGreetedStreamSessionId = signal.CurrentStreamSessionId;
                FirstTimeChatterDetectedEvent evt = new()
                {
                    BroadcasterId = broadcasterId,
                    ViewerUserId = signal.ViewerUserId,
                    ViewerExternalUserId = signal.ViewerExternalUserId,
                    ViewerDisplayName = signal.DisplayName,
                    OccurredAt = signal.At,
                };
                publish = pubCt => _eventBus.PublishAsync(evt, pubCt);
            }
        }
        else if (state.LastSeenStreamSessionId == signal.CurrentStreamSessionId)
        {
            // Same stream — already accounted for this session. State update only (LastChatAt).
            state.LastChatAt = signal.At;
        }
        else
        {
            // First message THIS stream, having chatted before → returning + streak update (D3).
            int daysSinceLastSeen = Math.Max(0, (signal.At.Date - state.LastChatAt.Date).Days);
            bool consecutive = await IsImmediatelyPreviousSessionAsync(
                broadcasterId,
                state.LastSeenStreamSessionId,
                signal.CurrentStreamSessionId,
                ct
            );
            state.ConsecutiveStreams = consecutive ? state.ConsecutiveStreams + 1 : 1;
            state.LastSeenStreamSessionId = signal.CurrentStreamSessionId;
            state.LastChatAt = signal.At;

            bool milestone =
                config.WatchStreakEnabled
                && IsMilestone(state.ConsecutiveStreams, config.StreakMilestonesJson);

            // One greeting per viewer per stream — the streak milestone takes precedence over the plain
            // returning greeting when both would fire, and both share the one greet-claim.
            if (
                (config.ReturningChatterEnabled || milestone)
                && TryClaimGreet(broadcasterId, config)
            )
            {
                state.LastGreetedStreamSessionId = signal.CurrentStreamSessionId;
                if (milestone)
                {
                    WatchStreakMilestoneEvent evt = new()
                    {
                        BroadcasterId = broadcasterId,
                        ViewerUserId = signal.ViewerUserId,
                        ViewerExternalUserId = signal.ViewerExternalUserId,
                        ViewerDisplayName = signal.DisplayName,
                        StreakCount = state.ConsecutiveStreams,
                        OccurredAt = signal.At,
                    };
                    publish = pubCt => _eventBus.PublishAsync(evt, pubCt);
                }
                else
                {
                    ReturningChatterDetectedEvent evt = new()
                    {
                        BroadcasterId = broadcasterId,
                        ViewerUserId = signal.ViewerUserId,
                        ViewerExternalUserId = signal.ViewerExternalUserId,
                        ViewerDisplayName = signal.DisplayName,
                        DaysSinceLastSeen = daysSinceLastSeen,
                        OccurredAt = signal.At,
                    };
                    publish = pubCt => _eventBus.PublishAsync(evt, pubCt);
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        if (publish is not null)
            await publish(ct);

        return Result.Success();
    }

    public async Task<Result<EngagementConfigDto>> GetConfigAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        EngagementConfig? config = await _db
            .EngagementConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BroadcasterId == broadcasterId, ct);
        return Result.Success(config is null ? Defaults() : ToDto(config));
    }

    public async Task<Result<EngagementConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        UpdateEngagementConfigRequest request,
        CancellationToken ct = default
    )
    {
        if (request.GreetCooldownSeconds < 0)
            return Result.Failure<EngagementConfigDto>(
                "GreetCooldownSeconds cannot be negative.",
                "VALIDATION_FAILED"
            );
        if (request.StreakMilestones is not null && request.StreakMilestones.Any(m => m <= 0))
            return Result.Failure<EngagementConfigDto>(
                "Streak milestones must be positive.",
                "VALIDATION_FAILED"
            );

        EngagementConfig? config = await _db.EngagementConfigs.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (config is null)
        {
            config = new EngagementConfig { BroadcasterId = broadcasterId };
            await _db.EngagementConfigs.AddAsync(config, ct);
        }

        config.FirstTimeChatterEnabled = request.FirstTimeChatterEnabled;
        config.ReturningChatterEnabled = request.ReturningChatterEnabled;
        config.WatchStreakEnabled = request.WatchStreakEnabled;
        config.GreetCooldownSeconds = request.GreetCooldownSeconds;
        config.StreakMilestonesJson = SerializeMilestones(request.StreakMilestones);
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(config));
    }

    // ─── Internals ────────────────────────────────────────────────────────────

    private static bool AnyEnabled(EngagementConfig c) =>
        c.FirstTimeChatterEnabled || c.ReturningChatterEnabled || c.WatchStreakEnabled;

    /// <summary>Claims the per-channel greet slot: true (and arms the cooldown) when free, false when a
    /// recent greeting is still cooling down (D4 burst limiter). A zero cooldown never blocks.</summary>
    private bool TryClaimGreet(Guid broadcasterId, EngagementConfig config)
    {
        string channelKey = broadcasterId.ToString();
        if (
            config.GreetCooldownSeconds > 0
            && _cooldowns.IsOnCooldown(channelKey, GreetCooldownKey)
        )
            return false;
        if (config.GreetCooldownSeconds > 0)
            _cooldowns.SetCooldown(
                channelKey,
                GreetCooldownKey,
                TimeSpan.FromSeconds(config.GreetCooldownSeconds)
            );
        return true;
    }

    /// <summary>
    /// True when <paramref name="lastSeenSessionId"/> is the session immediately before
    /// <paramref name="currentSessionId"/> in the channel's stream order (D3 "consecutive").
    /// </summary>
    private async Task<bool> IsImmediatelyPreviousSessionAsync(
        Guid broadcasterId,
        string? lastSeenSessionId,
        string currentSessionId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(lastSeenSessionId))
            return false;

        List<StreamOrder> sessions = await _db
            .Streams.Where(s => s.ChannelId == broadcasterId && s.StartedAt != null)
            .Select(s => new StreamOrder(s.Id, s.StartedAt))
            .ToListAsync(ct);

        StreamOrder? current = sessions.FirstOrDefault(s => s.Id == currentSessionId);
        if (current is null)
            return false;

        string? previousId = sessions
            .Where(s => s.StartedAt < current.StartedAt)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.Id)
            .FirstOrDefault();

        return previousId == lastSeenSessionId;
    }

    private sealed record StreamOrder(string Id, DateTimeOffset? StartedAt);

    private static bool IsMilestone(int streak, string? milestonesJson)
    {
        int[] milestones = DeserializeMilestones(milestonesJson);
        // Empty list = every stream is a milestone (engagement.md §1).
        return milestones.Length == 0 || milestones.Contains(streak);
    }

    private static int[] DeserializeMilestones(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? SerializeMilestones(IReadOnlyList<int>? milestones) =>
        milestones is null || milestones.Count == 0
            ? null
            : JsonSerializer.Serialize(milestones.Distinct().OrderBy(m => m).ToArray());

    private static EngagementConfigDto ToDto(EngagementConfig c) =>
        new(
            c.FirstTimeChatterEnabled,
            c.ReturningChatterEnabled,
            c.WatchStreakEnabled,
            DeserializeMilestones(c.StreakMilestonesJson),
            c.GreetCooldownSeconds
        );

    private static EngagementConfigDto Defaults() => new(false, false, false, [], 5);

    /// <summary>PII-hash of the viewer's platform id — a stable anonymized handle, never the raw id.</summary>
    private static string HashViewer(string externalUserId) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(externalUserId)
            )
        )[..32];
}
