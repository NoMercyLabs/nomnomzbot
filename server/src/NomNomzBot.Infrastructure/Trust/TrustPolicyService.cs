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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Application.Trust.Services;
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Infrastructure.Trust;

/// <summary>
/// Reads the channel's <see cref="TrustPolicy"/>, falling back to
/// <see cref="TrustScoreCalculator.DefaultPolicy"/> when the channel has never tuned anything.
/// </summary>
public sealed class TrustPolicyService : ITrustPolicyService
{
    private readonly IApplicationDbContext _db;

    public TrustPolicyService(IApplicationDbContext db) => _db = db;

    public async Task<TrustPolicy> GetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Cross-tenant-safe: scoring can run outside a resolved-tenant request (EventSub handlers,
        // background projections), so the broadcaster is matched explicitly rather than relying on the
        // ambient query filter. AsNoTracking because callers only read the values.
        TrustPolicy? stored = await _db
            .TrustPolicies.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.DeletedAt == null,
                cancellationToken
            );

        return stored ?? TrustScoreCalculator.DefaultPolicy;
    }

    public async Task<Result<TrustPolicyDto>> GetForEditingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        TrustPolicy? stored = await _db
            .TrustPolicies.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.DeletedAt == null,
                cancellationToken
            );

        return Result.Success(
            ToDto(stored ?? TrustScoreCalculator.DefaultPolicy, isPinned: stored is not null)
        );
    }

    public async Task<Result<TrustPolicyDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateTrustPolicyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Result validation = Validate(request);
        if (validation.IsFailure)
            return Result.Failure<TrustPolicyDto>(validation.ErrorMessage!, validation.ErrorCode!);

        TrustPolicy? policy = await _db
            .TrustPolicies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.BroadcasterId == broadcasterId && p.DeletedAt == null,
                cancellationToken
            );
        if (policy is null)
        {
            policy = new TrustPolicy { BroadcasterId = broadcasterId };
            _db.TrustPolicies.Add(policy);
        }

        policy.RequestCountWeight = request.RequestCountWeight;
        policy.AccountAgeWeight = request.AccountAgeWeight;
        policy.ContentAgeWeight = request.ContentAgeWeight;
        policy.ContentPopularityWeight = request.ContentPopularityWeight;
        policy.RequestCountDecay = request.RequestCountDecay;
        policy.AccountAgeDecay = request.AccountAgeDecay;
        policy.ContentAgeDecay = request.ContentAgeDecay;
        policy.ContentPopularityDecay = request.ContentPopularityDecay;
        policy.NotFollowingFactor = request.NotFollowingFactor;
        policy.ReputationBoostEnabled = request.ReputationBoostEnabled;
        policy.YouTubeQualityPenaltyFactor = request.YouTubeQualityPenaltyFactor;
        policy.SkipPenalty = request.SkipPenalty;
        policy.TimeoutPenalty = request.TimeoutPenalty;
        policy.BanPenalty = request.BanPenalty;
        policy.UntrustedMax = request.UntrustedMax;
        policy.LowMax = request.LowMax;
        policy.StandardMax = request.StandardMax;
        policy.HeatHalfLifeHours = request.HeatHalfLifeHours;
        policy.HeatDeltaBan = request.HeatDeltaBan;
        policy.HeatDeltaTimeout = request.HeatDeltaTimeout;
        policy.HeatDeltaReportValidated = request.HeatDeltaReportValidated;
        policy.HeatDeltaAutoModDenied = request.HeatDeltaAutoModDenied;
        policy.HeatDeltaFilterHit = request.HeatDeltaFilterHit;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(policy, isPinned: true));
    }

    /// <summary>
    /// Server-side ranges. The editor validates too, but a policy that cannot produce a sane score must
    /// never reach the scorer regardless of what posted it.
    /// </summary>
    private static Result Validate(UpdateTrustPolicyRequest r)
    {
        double weightSum =
            r.RequestCountWeight
            + r.AccountAgeWeight
            + r.ContentAgeWeight
            + r.ContentPopularityWeight;
        if (Math.Abs(weightSum - 1.0) > 0.0001)
            return Result.Failure(
                $"The four score weights must add up to 1.0 (they currently add up to {weightSum:0.####}).",
                "VALIDATION_FAILED"
            );

        if (
            r.RequestCountWeight < 0
            || r.AccountAgeWeight < 0
            || r.ContentAgeWeight < 0
            || r.ContentPopularityWeight < 0
        )
            return Result.Failure("A score weight cannot be negative.", "VALIDATION_FAILED");

        if (
            r.RequestCountDecay <= 0
            || r.AccountAgeDecay <= 0
            || r.ContentAgeDecay <= 0
            || r.ContentPopularityDecay <= 0
        )
            return Result.Failure(
                "Every growth speed must be greater than zero — a zero rate freezes that part of the score at 0.",
                "VALIDATION_FAILED"
            );

        if (r.NotFollowingFactor is < 0 or > 1)
            return Result.Failure(
                "The not-following penalty must be between 0 and 1 (1 disables it).",
                "VALIDATION_FAILED"
            );

        if (r.YouTubeQualityPenaltyFactor is < 0 or > 1)
            return Result.Failure(
                "The YouTube quality penalty must be between 0 and 1 (1 disables it).",
                "VALIDATION_FAILED"
            );

        if (r.SkipPenalty < 0 || r.TimeoutPenalty < 0 || r.BanPenalty < 0)
            return Result.Failure(
                "A violation penalty cannot be negative — that would REWARD the violation.",
                "VALIDATION_FAILED"
            );

        if (!(r.UntrustedMax < r.LowMax && r.LowMax < r.StandardMax))
            return Result.Failure(
                "The trust tier ceilings must increase: Untrusted below Low, Low below Standard.",
                "VALIDATION_FAILED"
            );

        if (r.UntrustedMax < 0 || r.StandardMax > 100)
            return Result.Failure(
                "Trust tier ceilings must sit within the 0–100 score range.",
                "VALIDATION_FAILED"
            );

        if (r.HeatHalfLifeHours <= 0)
            return Result.Failure(
                "The heat cool-down must be greater than zero hours, or heat would never decay.",
                "VALIDATION_FAILED"
            );

        if (
            r.HeatDeltaBan < 0
            || r.HeatDeltaTimeout < 0
            || r.HeatDeltaReportValidated < 0
            || r.HeatDeltaAutoModDenied < 0
            || r.HeatDeltaFilterHit < 0
        )
            return Result.Failure(
                "A heat amount cannot be negative — heat never accrues downward.",
                "VALIDATION_FAILED"
            );

        return Result.Success();
    }

    private static TrustPolicyDto ToDto(TrustPolicy p, bool isPinned) =>
        new(
            p.RequestCountWeight,
            p.AccountAgeWeight,
            p.ContentAgeWeight,
            p.ContentPopularityWeight,
            p.RequestCountDecay,
            p.AccountAgeDecay,
            p.ContentAgeDecay,
            p.ContentPopularityDecay,
            p.NotFollowingFactor,
            p.ReputationBoostEnabled,
            p.YouTubeQualityPenaltyFactor,
            p.SkipPenalty,
            p.TimeoutPenalty,
            p.BanPenalty,
            p.UntrustedMax,
            p.LowMax,
            p.StandardMax,
            p.HeatHalfLifeHours,
            p.HeatDeltaBan,
            p.HeatDeltaTimeout,
            p.HeatDeltaReportValidated,
            p.HeatDeltaAutoModDenied,
            p.HeatDeltaFilterHit,
            isPinned
        );
}
