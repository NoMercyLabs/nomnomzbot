// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// The spam-defence trust ladder (spam-defense.md §L4). Ladder-valued so comparisons are ordinary
/// numeric ones, matching the existing permission ladder. <b>Users never see these numbers</b> — the
/// dashboard shows tier NAMES, per the role-name rule.
/// </summary>
public enum SpamTrustTier
{
    /// <summary>Every unknown account. May always post plain text.</summary>
    Untrusted = 0,

    /// <summary>Account ≥ 7d and following ≥ 24h.</summary>
    Newcomer = 10,

    /// <summary>Account ≥ 30d, following ≥ 7d, ≥ 5 messages here.</summary>
    Known = 20,

    /// <summary>Account ≥ 6mo, following ≥ 30d, ≥ 50 messages, no upheld strike in 90d.</summary>
    Regular = 30,

    /// <summary>Positive standing anywhere (§L1.2). Never auto-banned or auto-timed-out.</summary>
    SemiTrusted = 40,

    /// <summary>Sub / VIP / mod in THIS channel, or granted by the operator.</summary>
    Trusted = 50,

    /// <summary>Immune (§L4.1). Every enforcement outcome reduces to Flag.</summary>
    Established = 60,
}

/// <summary>Things a message can do that a tier must have earned.</summary>
public enum SpamCapability
{
    PostPlainText,
    PostLink,
    MentionManyUsers,
    PasteLongText,
    EmoteOnlyMessage,
    NonLatinScript,
    CosmeticAbuseCharacters,
}

/// <summary>Per-channel participation, which is what earns Established — not account age.</summary>
public sealed record ChannelParticipation
{
    public double DaysSinceFirstMessageHere { get; init; }
    public int MessagesHere { get; init; }
    public int DistinctActiveDaysHere { get; init; }
    public double DaysSinceLastUpheldStrike { get; init; } = double.MaxValue;
    public bool OperatorGrantedEstablished { get; init; }
    public bool IsModeratorHere { get; init; }
    public bool IsVipHere { get; init; }
    public bool IsSubscriberHere { get; init; }
    public int MessageCountHere { get; init; }
}

/// <summary>
/// The per-channel thresholds an operator may tune (§L4.1: "thresholds are per-channel tunable; the
/// invariant is not"). Defaults are the spec's.
/// </summary>
public sealed record TrustTierThresholds
{
    public double EstablishedDays { get; init; } = 90;
    public int EstablishedMessages { get; init; } = 300;
    public int EstablishedDistinctActiveDays { get; init; } = 30;
    public double EstablishedStrikeFreeDays { get; init; } = 180;
}

/// <summary>
/// Resolves a viewer's tier and answers what that tier may do (spam-defense.md §L4).
///
/// <para><b>Established is a short-circuit, not a high bar.</b> §L4.1 is explicit that immunity is not a
/// lower score, a higher threshold, or a heavy negative weight that a loud enough stack of signals could
/// still overcome. <see cref="IsImmune"/> is therefore checked BEFORE any scoring runs, so no future
/// signal, no network signature and no correlation cohort can reach an Established viewer. The failure it
/// prevents — a channel's regular of three years banned automatically for pasting a zalgo meme, or swept
/// into a cohort because they quoted the spam to complain about it — is worse than every spam message this
/// system will ever catch.</para>
/// </summary>
public static class TrustTierLadder
{
    /// <summary>
    /// The shipped capability floors (§L4). Editable per channel by the operator; the non-Latin gate
    /// defaults OFF (Regular) because Japanese, Korean, Cyrillic and Arabic chat is written by real
    /// viewers, and a streamer must never be "protected" into silencing their own audience.
    /// </summary>
    public static readonly IReadOnlyDictionary<
        SpamCapability,
        SpamTrustTier
    > DefaultCapabilityFloors = new Dictionary<SpamCapability, SpamTrustTier>
    {
        [SpamCapability.PostPlainText] = SpamTrustTier.Untrusted,
        [SpamCapability.PostLink] = SpamTrustTier.Known,
        [SpamCapability.MentionManyUsers] = SpamTrustTier.Newcomer,
        [SpamCapability.PasteLongText] = SpamTrustTier.Newcomer,
        [SpamCapability.EmoteOnlyMessage] = SpamTrustTier.Newcomer,
        [SpamCapability.NonLatinScript] = SpamTrustTier.Regular,
        // "Never, at any tier" means no EARNABLE tier grants it. It does not override SD8: for an
        // Established viewer these characters flag and nothing more.
        [SpamCapability.CosmeticAbuseCharacters] = SpamTrustTier.Established,
    };

    /// <summary>
    /// Resolve the tier. Established is tested first, then in-channel standing, then instance-wide
    /// standing, then the earned ladder — highest wins.
    /// </summary>
    public static SpamTrustTier Resolve(
        AccountFacts account,
        ChannelParticipation participation,
        AccountRiskAssessment risk,
        TrustTierThresholds? thresholds = null
    )
    {
        TrustTierThresholds t = thresholds ?? new TrustTierThresholds();

        if (IsEstablished(participation, t))
            return SpamTrustTier.Established;

        if (
            participation.IsModeratorHere
            || participation.IsVipHere
            || participation.IsSubscriberHere
        )
            return SpamTrustTier.Trusted;

        if (risk.IsSemiTrusted)
            return SpamTrustTier.SemiTrusted;

        if (
            account.AccountAgeDays >= 182
            && account.IsFollowing
            && account.FollowAgeHours >= 30 * 24
            && participation.MessageCountHere >= 50
            && participation.DaysSinceLastUpheldStrike >= 90
        )
            return SpamTrustTier.Regular;

        if (
            account.AccountAgeDays >= 30
            && account.IsFollowing
            && account.FollowAgeHours >= 7 * 24
            && participation.MessageCountHere >= 5
        )
            return SpamTrustTier.Known;

        if (account.AccountAgeDays >= 7 && account.IsFollowing && account.FollowAgeHours >= 24)
            return SpamTrustTier.Newcomer;

        return SpamTrustTier.Untrusted;
    }

    /// <summary>
    /// §L4.1. Earned by sustained participation IN THIS CHANNEL — a ten-year-old account that has never
    /// spoken here is Untrusted, correctly. Moderators and VIPs are granted it implicitly, and an operator
    /// may grant it by hand.
    /// </summary>
    public static bool IsEstablished(ChannelParticipation p, TrustTierThresholds? thresholds = null)
    {
        TrustTierThresholds t = thresholds ?? new TrustTierThresholds();

        if (p.OperatorGrantedEstablished || p.IsModeratorHere || p.IsVipHere)
            return true;

        return p.DaysSinceFirstMessageHere >= t.EstablishedDays
            && p.MessagesHere >= t.EstablishedMessages
            && p.DistinctActiveDaysHere >= t.EstablishedDistinctActiveDays
            && p.DaysSinceLastUpheldStrike >= t.EstablishedStrikeFreeDays;
    }

    /// <summary>
    /// The SD8 short-circuit. Called BEFORE the scorer, never after — an engine that can act before it can
    /// be immune has a window in which it will hurt someone.
    /// </summary>
    public static bool IsImmune(SpamTrustTier tier) => tier == SpamTrustTier.Established;

    /// <summary>
    /// SD11's ceiling: a viewer at or above Semi-Trusted is never auto-banned or auto-timed-out. The
    /// engine may delete and flag; a human decides everything past that.
    /// </summary>
    public static bool IsShieldedFromAutomatedAccountAction(SpamTrustTier tier) =>
        tier >= SpamTrustTier.SemiTrusted;

    /// <summary>Whether <paramref name="tier"/> has earned <paramref name="capability"/>.</summary>
    public static bool Allows(
        SpamTrustTier tier,
        SpamCapability capability,
        IReadOnlyDictionary<SpamCapability, SpamTrustTier>? floors = null
    )
    {
        IReadOnlyDictionary<SpamCapability, SpamTrustTier> table =
            floors ?? DefaultCapabilityFloors;
        return !table.TryGetValue(capability, out SpamTrustTier required) || tier >= required;
    }
}
