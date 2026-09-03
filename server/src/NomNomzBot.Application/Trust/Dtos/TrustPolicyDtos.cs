// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Trust.Dtos;

/// <summary>
/// A channel's trust tuning as the dashboard sees it (S-OWN23). Carries VALUES only — every label and
/// plain-language explanation is frontend i18n, per the translations-never-in-code rule.
/// <see cref="IsPinned"/> is false while the channel is still tracking the shipped defaults, so the UI
/// can show "default" versus "you changed this" without guessing from the numbers.
/// </summary>
public sealed record TrustPolicyDto(
    double RequestCountWeight,
    double AccountAgeWeight,
    double ContentAgeWeight,
    double ContentPopularityWeight,
    double RequestCountDecay,
    double AccountAgeDecay,
    double ContentAgeDecay,
    double ContentPopularityDecay,
    double NotFollowingFactor,
    bool ReputationBoostEnabled,
    double YouTubeQualityPenaltyFactor,
    double SkipPenalty,
    double TimeoutPenalty,
    double BanPenalty,
    double UntrustedMax,
    double LowMax,
    double StandardMax,
    double HeatHalfLifeHours,
    decimal HeatDeltaBan,
    decimal HeatDeltaTimeout,
    decimal HeatDeltaReportValidated,
    decimal HeatDeltaAutoModDenied,
    decimal HeatDeltaFilterHit,
    bool IsPinned
);

/// <summary>
/// A trust-tuning save. Every field is required: the editor always posts the full policy, so a partial
/// body can never half-apply a set of weights that must stay consistent with each other.
/// </summary>
public sealed record UpdateTrustPolicyRequest(
    double RequestCountWeight,
    double AccountAgeWeight,
    double ContentAgeWeight,
    double ContentPopularityWeight,
    double RequestCountDecay,
    double AccountAgeDecay,
    double ContentAgeDecay,
    double ContentPopularityDecay,
    double NotFollowingFactor,
    bool ReputationBoostEnabled,
    double YouTubeQualityPenaltyFactor,
    double SkipPenalty,
    double TimeoutPenalty,
    double BanPenalty,
    double UntrustedMax,
    double LowMax,
    double StandardMax,
    double HeatHalfLifeHours,
    decimal HeatDeltaBan,
    decimal HeatDeltaTimeout,
    decimal HeatDeltaReportValidated,
    decimal HeatDeltaAutoModDenied,
    decimal HeatDeltaFilterHit
);

/// <summary>
/// The channel's REAL Twitch AutoMod levels (0–4 per category, or one overall level driving them all).
/// Distinct from the bot's own <c>AutomodConfigDto</c> filters — this is Twitch's own pre-publish hold,
/// the only lever that stops a message before chat ever sees it.
/// </summary>
public sealed record TwitchAutoModSettingsDto(
    int? OverallLevel,
    int Aggression,
    int Bullying,
    int Disability,
    int Misogyny,
    int RaceEthnicityOrReligion,
    int SexBasedTerms,
    int SexualitySexOrGender,
    int Swearing
);

/// <summary>
/// Update the channel's Twitch AutoMod levels. Twitch treats <c>OverallLevel</c> and the per-category
/// values as mutually exclusive: set the overall dial, or set categories individually — never both.
/// </summary>
public sealed record UpdateTwitchAutoModSettingsRequest(
    int? OverallLevel = null,
    int? Aggression = null,
    int? Bullying = null,
    int? Disability = null,
    int? Misogyny = null,
    int? RaceEthnicityOrReligion = null,
    int? SexBasedTerms = null,
    int? SexualitySexOrGender = null,
    int? Swearing = null
);
