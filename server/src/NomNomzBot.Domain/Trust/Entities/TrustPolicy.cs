// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Trust.Entities;

/// <summary>
/// The channel's trust and heat tuning (S-OWN23). Every constant that used to be a
/// <c>private const</c> inside <see cref="TrustScoreCalculator"/> lives here instead, so an operator
/// can see and change what the bot decides automatically. One policy per channel; every default below
/// is EXACTLY the value the calculator shipped with, so an untouched policy reproduces today's scores
/// byte for byte.
///
/// <para><b>Blast radius, stated exactly (traced 2026-09-03, unified 2026-09-04):</b> this policy drives
/// the moderation trust/heat projection (<c>UserTrustScore</c>) — the scores and tiers the moderation
/// surfaces show. There is now exactly ONE trust engine: the second implementation that used to sit in
/// <c>Infrastructure/Music/TrustService</c> (a 0.0–1.0 scale with its own hardcoded lambdas) had no
/// caller anywhere in the product and is deleted, along with the <c>MusicService.CheckTrustPermission</c>
/// method that would have bridged them. Song requests are not trust-gated today; when they are, they
/// gate on THIS policy through <see cref="TrustScoreCalculator"/>, never on a second set of weights.</para>
///
/// <para>Per <c>spam-defense.md</c> §6 these are tunables, not invariants: no value here may switch
/// off the protections that exist so a regular is never auto-punished.</para>
/// </summary>
public class TrustPolicy : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One policy per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    // ─── Score weights — validated server-side to sum to 1.0 ──────────────────

    /// <summary>How much the user's successful-request history counts.</summary>
    public double RequestCountWeight { get; set; } = 0.25;

    /// <summary>How much the age of the user's platform account counts.</summary>
    public double AccountAgeWeight { get; set; } = 0.25;

    /// <summary>How much the age of the requested content counts (song requests).</summary>
    public double ContentAgeWeight { get; set; } = 0.30;

    /// <summary>How much the popularity of the requested content counts (song requests).</summary>
    public double ContentPopularityWeight { get; set; } = 0.20;

    // ─── Decay rates — higher saturates toward 100 sooner ─────────────────────

    /// <summary>Default 0.599 — about 5 successful requests reach ~95% of this metric.</summary>
    public double RequestCountDecay { get; set; } = 0.599;

    /// <summary>Default 0.499 — about 6 months of account age reaches ~95%.</summary>
    public double AccountAgeDecay { get; set; } = 0.499;

    /// <summary>Default 0.999 — about 3 months of content age reaches ~95%.</summary>
    public double ContentAgeDecay { get; set; } = 0.999;

    /// <summary>Default 0.0003 — about 10 000 views reaches ~95%.</summary>
    public double ContentPopularityDecay { get; set; } = 0.0003;

    // ─── Multipliers and boosts ───────────────────────────────────────────────

    /// <summary>Score multiplier when the user does not follow, or followed under 24h ago. 1.0 disables it.</summary>
    public double NotFollowingFactor { get; set; } = 0.75;

    /// <summary>Whether mods, VIPs, subscribers and established requesters get the halfway-to-100 boost.</summary>
    public bool ReputationBoostEnabled { get; set; } = true;

    /// <summary>
    /// Multiplier applied per failed YouTube channel-quality check (thin catalogue, few subscribers,
    /// brand-new channel). Applies to song-request content only. 1.0 disables the penalties.
    /// </summary>
    public double YouTubeQualityPenaltyFactor { get; set; } = 0.75;

    // ─── Violation penalties — flat points removed after boosts ───────────────

    /// <summary>Points removed each time a moderator skips this user's request.</summary>
    public double SkipPenalty { get; set; } = 5.0;

    /// <summary>Points removed per timeout on this user.</summary>
    public double TimeoutPenalty { get; set; } = 10.0;

    /// <summary>Points removed per ban on this user — the heaviest penalty.</summary>
    public double BanPenalty { get; set; } = 30.0;

    // ─── Tier ceilings — users see tier NAMES, never these numbers ────────────

    /// <summary>Scores at or below this are Untrusted.</summary>
    public double UntrustedMax { get; set; } = 25.0;

    /// <summary>Scores above <see cref="UntrustedMax"/> up to this are Low trust.</summary>
    public double LowMax { get; set; } = 50.0;

    /// <summary>Scores above <see cref="LowMax"/> up to this are Standard; anything higher is Trusted.</summary>
    public double StandardMax { get; set; } = 75.0;

    // ─── Heat — recent bad behaviour, decaying ────────────────────────────────

    /// <summary>Hours after which half of a user's accumulated heat has decayed away.</summary>
    public double HeatHalfLifeHours { get; set; } = 24.0;

    /// <summary>Heat added when this user is banned.</summary>
    public decimal HeatDeltaBan { get; set; } = 40m;

    /// <summary>Heat added when this user is timed out.</summary>
    public decimal HeatDeltaTimeout { get; set; } = 15m;

    /// <summary>Heat added when a viewer report against this user is upheld.</summary>
    public decimal HeatDeltaReportValidated { get; set; } = 10m;

    /// <summary>Heat added when one of this user's AutoMod-held messages is denied.</summary>
    public decimal HeatDeltaAutoModDenied { get; set; } = 5m;

    /// <summary>Heat added when this user trips a chat filter.</summary>
    public decimal HeatDeltaFilterHit { get; set; } = 5m;

    public int ConfigSchemaVersion { get; set; } = 1;
}
