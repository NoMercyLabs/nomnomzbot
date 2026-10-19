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
/// <see cref="TrustScoreCalculator.DefaultPolicy"/> (built from <see cref="TrustPolicyDefaults"/>) when
/// the channel has never tuned anything.
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

    public TrustPolicyDto GetDefaults() => DefaultsDto();

    /// <summary>The shipped defaults as the dashboard sees them; never pinned.</summary>
    public static TrustPolicyDto DefaultsDto() =>
        ToDto(TrustScoreCalculator.DefaultPolicy, isPinned: false);

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

        return Result.Success(stored is null ? DefaultsDto() : ToDto(stored, isPinned: true));
    }

    public async Task<Result<TrustPolicyDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateTrustPolicyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Result validation = TrustPolicyValidator.Validate(request);
        if (validation.IsFailure)
            return Result.Failure<TrustPolicyDto>(
                validation.ErrorMessage!,
                validation.ErrorCode!,
                errorData: validation.ErrorData
            );

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
        policy.AffiliateBoost = request.AffiliateBoost;
        policy.PartnerBoost = request.PartnerBoost;
        policy.VerifiedYouTubeBoost = request.VerifiedYouTubeBoost;
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
            p.AffiliateBoost,
            p.PartnerBoost,
            p.VerifiedYouTubeBoost,
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
