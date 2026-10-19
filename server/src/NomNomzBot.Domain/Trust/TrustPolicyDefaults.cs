// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Trust;

/// <summary>
/// The one place every trust-tuning default and every accepted bound is written down. The
/// <see cref="Entities.TrustPolicy"/> initializers, the EF column defaults, the calculator's fallback,
/// the validator and the defaults the dashboard shows all read these constants, so a default can never
/// drift between a stored row, a missing row and the editor.
/// </summary>
public static class TrustPolicyDefaults
{
    // Score weights (the four must sum to 1.0)
    public const double RequestCountWeight = 0.25;
    public const double AccountAgeWeight = 0.25;
    public const double ContentAgeWeight = 0.30;
    public const double ContentPopularityWeight = 0.20;

    // Decay rates
    public const double RequestCountDecay = 0.599;
    public const double AccountAgeDecay = 0.499;
    public const double ContentAgeDecay = 0.999;
    public const double ContentPopularityDecay = 0.0003;

    // Multipliers and boosts
    public const double NotFollowingFactor = 0.75;
    public const bool ReputationBoostEnabled = true;
    public const double AffiliateBoost = 5.0;
    public const double PartnerBoost = 10.0;
    public const double VerifiedYouTubeBoost = 10.0;
    public const double YouTubeQualityPenaltyFactor = 0.75;

    // Violation penalties
    public const double SkipPenalty = 5.0;
    public const double TimeoutPenalty = 10.0;
    public const double BanPenalty = 30.0;

    // Tier ceilings
    public const double UntrustedMax = 25.0;
    public const double LowMax = 50.0;
    public const double StandardMax = 75.0;

    // Heat
    public const double HeatHalfLifeHours = 24.0;
    public const decimal HeatDeltaBan = 40m;
    public const decimal HeatDeltaTimeout = 15m;
    public const decimal HeatDeltaReportValidated = 10m;
    public const decimal HeatDeltaAutoModDenied = 5m;
    public const decimal HeatDeltaFilterHit = 5m;

    // Accepted bounds (a value outside these cannot produce a sane score)
    public const double MaxDecay = 10.0;
    public const double MaxScore = 100.0;
    public const double MaxHeatHalfLifeHours = 24.0 * 365.0;
    public const decimal MaxHeatDelta = 1000m;
}
