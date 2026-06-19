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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Trust scoring using Bamo's exponential decay algorithm.
///
/// Trust score (0.0 – 1.0) is computed from four components:
///
///   requestScore    = exp(-λ_r × requestCount)         ; decays as user requests more
///   accountScore    = 1 - exp(-λ_a × accountAgeDays)   ; grows with account age
///   contentScore    = exp(-λ_c × violationCount)        ; decays with violations
///   popularityScore = 1 - exp(-λ_p × followAgeDays)    ; grows with follow age
///
/// Final score = mean(requestScore, accountScore, contentScore, popularityScore)
///             × followagePenalty   (0.75 if follow age < 7 days)
///
/// Reputation boost (positive interaction):
///   newScore = score + (1 - score) × 0.5               ; gap-halving towards 1.0
///
/// Violation penalty:
///   newContentScore = contentScore × (1 - violationPenalty)   ; default 0.3
///
/// State is persisted in the Record table as JSON blobs (RecordType = "trust_state").
/// Hot reads hit an in-memory cache (expires after 10 minutes of inactivity).
/// </summary>
public sealed class TrustService : ITrustService
{
    private const string RecordType = "trust_state";

    // Exponential decay lambdas (tune these to taste)
    private const double LambdaRequest = 0.01; // slow decay as requests accumulate
    private const double LambdaAccount = 0.005; // grows over ~200 days to near-1
    private const double LambdaContent = 0.5; // fast decay with each violation
    private const double LambdaPopularity = 0.02; // grows over ~50 days follow age

    private const double FollowagePenaltyThresholdDays = 7;
    private const double FollowagePenaltyMultiplier = 0.75;
    private const double ViolationPenalty = 0.3;
    private const double ReputationBoostFraction = 0.5; // gap-halving

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<TrustService> _logger;

    // In-memory state cache: key = "broadcasterId:userId"
    private readonly ConcurrentDictionary<string, TrustState> _stateCache = new();

    public TrustService(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<TrustService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public async Task<double> GetScoreAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    )
    {
        TrustState state = await GetOrLoadStateAsync(broadcasterId, userId, ct);
        return ComputeScore(state);
    }

    public async Task RecordViolationAsync(
        string broadcasterId,
        string userId,
        string violationType,
        CancellationToken ct = default
    )
    {
        TrustState state = await GetOrLoadStateAsync(broadcasterId, userId, ct);

        state.ViolationCount++;
        state.ContentScore = state.ContentScore * (1 - ViolationPenalty);
        state.LastViolationAt = DateTime.UtcNow;
        state.LastViolationType = violationType;

        await SaveStateAsync(broadcasterId, userId, state, ct);
        _logger.LogDebug(
            "Trust violation recorded: {User} in {Channel} ({Type}) — new score {Score:F3}",
            userId,
            broadcasterId,
            violationType,
            ComputeScore(state)
        );
    }

    public async Task RecordPositiveInteractionAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct = default
    )
    {
        TrustState state = await GetOrLoadStateAsync(broadcasterId, userId, ct);

        double currentScore = ComputeScore(state);
        double boostedScore = currentScore + (1.0 - currentScore) * ReputationBoostFraction;

        // Distribute the boost proportionally across contentScore (primary reputation signal)
        state.ContentScore = Math.Min(1.0, state.ContentScore + (boostedScore - currentScore));

        await SaveStateAsync(broadcasterId, userId, state, ct);
    }

    // ─── Score computation ────────────────────────────────────────────────────

    private double ComputeScore(TrustState state)
    {
        // requestScore: decays as the user has queued more items
        double requestScore = Math.Exp(-LambdaRequest * state.RequestCount);

        // accountScore: grows with Twitch account age
        double accountAgeDays = (DateTime.UtcNow - state.AccountCreatedAt).TotalDays;
        double accountScore = 1.0 - Math.Exp(-LambdaAccount * accountAgeDays);

        // contentScore: stored directly (maintained by violations/boosts)
        double contentScore = Math.Clamp(state.ContentScore, 0.0, 1.0);

        // popularityScore: grows with follow age
        double followAgeDays = state.FollowedAt.HasValue
            ? (DateTime.UtcNow - state.FollowedAt.Value).TotalDays
            : 0;
        double popularityScore = 1.0 - Math.Exp(-LambdaPopularity * followAgeDays);

        double baseScore = (requestScore + accountScore + contentScore + popularityScore) / 4.0;

        // Apply followage penalty for new followers (< 7 days)
        if (followAgeDays < FollowagePenaltyThresholdDays)
            baseScore *= FollowagePenaltyMultiplier;

        return Math.Clamp(baseScore, 0.0, 1.0);
    }

    // ─── State persistence ────────────────────────────────────────────────────

    private async Task<TrustState> GetOrLoadStateAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct
    )
    {
        string cacheKey = $"{broadcasterId}:{userId}";

        if (
            _stateCache.TryGetValue(cacheKey, out TrustState? cached)
            && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(10)
        )
        {
            return cached;
        }

        TrustState state =
            await LoadFromDbAsync(broadcasterId, userId, ct)
            ?? await BuildInitialStateAsync(broadcasterId, userId, ct);

        state.CachedAt = DateTime.UtcNow;
        _stateCache[cacheKey] = state;
        return state;
    }

    private async Task<TrustState?> LoadFromDbAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            Record? record = await db.Records.FirstOrDefaultAsync(
                r =>
                    r.BroadcasterId == broadcasterId
                    && r.UserId == userId
                    && r.RecordType == RecordType,
                ct
            );

            if (record is null)
                return null;

            TrustState? state = JsonSerializer.Deserialize<TrustState>(record.Data);
            if (state is not null)
                state.RecordId = record.Id;
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to load trust state for {UserId} in {BroadcasterId}",
                userId,
                broadcasterId
            );
            return null;
        }
    }

    private async Task<TrustState> BuildInitialStateAsync(
        string broadcasterId,
        string userId,
        CancellationToken ct
    )
    {
        // Try to seed from User entity for account age
        DateTime accountCreated = DateTime.UtcNow.AddDays(-1); // default: 1-day-old account
        DateTime? followedAt = null;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            User? user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is not null)
                accountCreated = user.CreatedAt;
        }
        catch
        {
            // Non-critical — use default
        }

        return new()
        {
            AccountCreatedAt = accountCreated,
            FollowedAt = followedAt,
            ContentScore = 0.8, // Start with moderate content trust
            RequestCount = 0,
            ViolationCount = 0,
        };
    }

    private async Task SaveStateAsync(
        string broadcasterId,
        string userId,
        TrustState state,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            string data = JsonSerializer.Serialize(state);

            if (state.RecordId.HasValue)
            {
                Record? record = await db.Records.FindAsync([state.RecordId.Value], ct);
                if (record is not null)
                {
                    record.Data = data;
                    record.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return;
                }
            }

            Record newRecord = new()
            {
                BroadcasterId = broadcasterId,
                UserId = userId,
                RecordType = RecordType,
                Data = data,
            };
            db.Records.Add(newRecord);
            await db.SaveChangesAsync(ct);
            state.RecordId = newRecord.Id;

            string cacheKey = $"{broadcasterId}:{userId}";
            state.CachedAt = DateTime.UtcNow;
            _stateCache[cacheKey] = state;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist trust state for {UserId}", userId);
        }
    }

    // ─── State model ──────────────────────────────────────────────────────────

    private sealed class TrustState
    {
        public int? RecordId { get; set; }
        public DateTime AccountCreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FollowedAt { get; set; }
        public double ContentScore { get; set; } = 0.8;
        public int RequestCount { get; set; }
        public int ViolationCount { get; set; }
        public DateTime? LastViolationAt { get; set; }
        public string? LastViolationType { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime CachedAt { get; set; }
    }
}
