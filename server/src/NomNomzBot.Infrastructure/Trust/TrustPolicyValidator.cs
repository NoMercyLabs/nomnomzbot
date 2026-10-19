// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Domain.Trust;

namespace NomNomzBot.Infrastructure.Trust;

/// <summary>
/// Server-side ranges for a trust-tuning save. The editor validates too, but a policy that cannot
/// produce a sane score must never reach the scorer regardless of what posted it. Every refusal carries
/// a <see cref="TrustPolicyViolation"/> (translatable key plus the field to mark) and keeps the English
/// message only as a fallback.
/// </summary>
internal static class TrustPolicyValidator
{
    private const string Code = "VALIDATION_FAILED";

    public static Result Validate(UpdateTrustPolicyRequest r)
    {
        (string Field, double Value)[] weights =
        [
            ("requestCountWeight", r.RequestCountWeight),
            ("accountAgeWeight", r.AccountAgeWeight),
            ("contentAgeWeight", r.ContentAgeWeight),
            ("contentPopularityWeight", r.ContentPopularityWeight),
        ];
        (string Field, double Value)[] decays =
        [
            ("requestCountDecay", r.RequestCountDecay),
            ("accountAgeDecay", r.AccountAgeDecay),
            ("contentAgeDecay", r.ContentAgeDecay),
            ("contentPopularityDecay", r.ContentPopularityDecay),
        ];
        (string Field, double Value)[] factors =
        [
            ("notFollowingFactor", r.NotFollowingFactor),
            ("youTubeQualityPenaltyFactor", r.YouTubeQualityPenaltyFactor),
        ];
        (string Field, double Value)[] boosts =
        [
            ("affiliateBoost", r.AffiliateBoost),
            ("partnerBoost", r.PartnerBoost),
            ("verifiedYouTubeBoost", r.VerifiedYouTubeBoost),
        ];
        (string Field, double Value)[] penalties =
        [
            ("skipPenalty", r.SkipPenalty),
            ("timeoutPenalty", r.TimeoutPenalty),
            ("banPenalty", r.BanPenalty),
        ];
        (string Field, double Value)[] tiers =
        [
            ("untrustedMax", r.UntrustedMax),
            ("lowMax", r.LowMax),
            ("standardMax", r.StandardMax),
        ];
        (string Field, decimal Value)[] heatDeltas =
        [
            ("heatDeltaBan", r.HeatDeltaBan),
            ("heatDeltaTimeout", r.HeatDeltaTimeout),
            ("heatDeltaReportValidated", r.HeatDeltaReportValidated),
            ("heatDeltaAutoModDenied", r.HeatDeltaAutoModDenied),
            ("heatDeltaFilterHit", r.HeatDeltaFilterHit),
        ];

        // NaN and infinity slip through every < / > comparison below, so they are refused first.
        foreach (
            (string field, double value) in weights
                .Concat(decays)
                .Concat(factors)
                .Concat(boosts)
                .Concat(penalties)
                .Concat(tiers)
                .Append(("heatHalfLifeHours", r.HeatHalfLifeHours))
        )
        {
            if (!double.IsFinite(value))
                return Refuse(
                    TrustPolicyViolation.NotFinite,
                    field,
                    "Every trust setting must be a finite number."
                );
        }

        foreach ((string field, double value) in weights)
        {
            if (value is < 0 or > 1)
                return Refuse(
                    TrustPolicyViolation.WeightOutOfRange,
                    field,
                    "A score weight must be between 0 and 1."
                );
        }

        double weightSum = weights.Sum(w => w.Value);
        if (Math.Abs(weightSum - 1.0) > 0.0001)
            return Refuse(
                TrustPolicyViolation.WeightsMustSumToOne,
                weights[0].Field,
                $"The four score weights must add up to 1.0 (they currently add up to {weightSum:0.####})."
            );

        foreach ((string field, double value) in decays)
        {
            if (value is <= 0 or > TrustPolicyDefaults.MaxDecay)
                return Refuse(
                    TrustPolicyViolation.DecayOutOfRange,
                    field,
                    $"Every growth speed must be greater than zero and at most {TrustPolicyDefaults.MaxDecay}."
                );
        }

        foreach ((string field, double value) in factors)
        {
            if (value is < 0 or > 1)
                return Refuse(
                    TrustPolicyViolation.FactorOutOfRange,
                    field,
                    "A penalty multiplier must be between 0 and 1 (1 disables it)."
                );
        }

        foreach ((string field, double value) in boosts)
        {
            if (value is < 0 or > TrustPolicyDefaults.MaxScore)
                return Refuse(
                    TrustPolicyViolation.BoostOutOfRange,
                    field,
                    "A platform-standing boost must be between 0 and 100 points (0 disables it)."
                );
        }

        foreach ((string field, double value) in penalties)
        {
            if (value is < 0 or > TrustPolicyDefaults.MaxScore)
                return Refuse(
                    TrustPolicyViolation.PenaltyOutOfRange,
                    field,
                    "A violation penalty must be between 0 and 100 points."
                );
        }

        if (r.UntrustedMax >= r.LowMax)
            return Refuse(
                TrustPolicyViolation.TiersMustAscend,
                "lowMax",
                "The trust tier ceilings must increase: Untrusted below Low, Low below Standard."
            );
        if (r.LowMax >= r.StandardMax)
            return Refuse(
                TrustPolicyViolation.TiersMustAscend,
                "standardMax",
                "The trust tier ceilings must increase: Untrusted below Low, Low below Standard."
            );

        if (r.UntrustedMax < 0)
            return Refuse(
                TrustPolicyViolation.TierOutOfRange,
                "untrustedMax",
                "Trust tier ceilings must sit within the 0-100 score range."
            );
        if (r.StandardMax > TrustPolicyDefaults.MaxScore)
            return Refuse(
                TrustPolicyViolation.TierOutOfRange,
                "standardMax",
                "Trust tier ceilings must sit within the 0-100 score range."
            );

        if (r.HeatHalfLifeHours is <= 0 or > TrustPolicyDefaults.MaxHeatHalfLifeHours)
            return Refuse(
                TrustPolicyViolation.HeatHalfLifeOutOfRange,
                "heatHalfLifeHours",
                "The heat cool-down must be greater than zero hours and at most one year."
            );

        foreach ((string field, decimal value) in heatDeltas)
        {
            if (value is < 0 or > TrustPolicyDefaults.MaxHeatDelta)
                return Refuse(
                    TrustPolicyViolation.HeatDeltaOutOfRange,
                    field,
                    $"A heat amount must be between 0 and {TrustPolicyDefaults.MaxHeatDelta}."
                );
        }

        return Result.Success();
    }

    private static Result Refuse(string key, string field, string message) =>
        Result.Failure(message, Code, errorData: new TrustPolicyViolation(key, field));
}
