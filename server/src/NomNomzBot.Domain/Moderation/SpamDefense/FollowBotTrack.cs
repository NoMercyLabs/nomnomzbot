// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>Per-account evidence that an account is a follow bot. Never "it followed during the spike".</summary>
public enum FollowBotIndicator
{
    /// <summary>The account id appears on a known-bot list.</summary>
    KnownBotId,

    /// <summary>The handle matches a bulk-generation pattern (<c>viewer80421933</c>).</summary>
    GeneratedHandlePattern,

    /// <summary>Created hours ago with no profile, no history, and nothing else to show.</summary>
    ZeroHistoryFreshAccount,

    /// <summary>Followed and unfollowed repeatedly — the shape of a farm cycling its inventory.</summary>
    FollowUnfollowOscillation,
}

/// <summary>What is known about one account that followed during a spike.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Username">Handle, checked for bulk-generation patterns.</param>
/// <param name="AccountAgeHours">Age at follow time.</param>
/// <param name="HasProfileContent">Bio, avatar, any history at all.</param>
/// <param name="FollowUnfollowCycles">Times this account has followed and unfollowed this channel.</param>
/// <param name="IsOnKnownBotList">Listed by the network feed.</param>
/// <param name="Tier">Trust tier — standing excludes an account from every sweep (SD11).</param>
public sealed record FollowCandidate(
    string AccountId,
    string Username,
    double AccountAgeHours,
    bool HasProfileContent,
    int FollowUnfollowCycles,
    bool IsOnKnownBotList,
    SpamTrustTier Tier
);

/// <summary>
/// One account found blockable, with the reason. The reason is required and non-empty by construction —
/// per SD9 a block with no per-account evidence must not be representable.
/// </summary>
public sealed record FollowBotFinding(
    string AccountId,
    IReadOnlyList<FollowBotIndicator> Indicators
);

/// <summary>
/// A reviewable, bulk-reversible batch of blocks from one spike. Nothing here is silently
/// unrecoverable: if a viral moment was misread, the operator restores the whole batch in one action.
/// </summary>
/// <param name="Findings">Only the accounts with their own evidence.</param>
/// <param name="Examined">How many accounts the spike window contained.</param>
public sealed record FollowBotBlockBatch(IReadOnlyList<FollowBotFinding> Findings, int Examined)
{
    /// <summary>Accounts examined and deliberately left alone. The number that proves SD9 is working.</summary>
    public int LeftAlone => Examined - Findings.Count;
}

/// <summary>
/// The follow-bot track (spam-defense.md §L3, §L3.1). It <b>blocks</b> rather than bans — a ban on a
/// silent account is wasted work — and it blocks only accounts that produced their own evidence.
///
/// <para><b>The lurker is the easiest person in this system to hurt by accident</b>, because they
/// generate no evidence of being real. A follow spike is exactly what a raid, a host, or a front-page
/// moment looks like. So the spike selects a window to look at, and this type then requires a finding
/// for each individual account. There is no path from "you followed at 14:03:07" to "you were blocked".
/// Being silent is not a signal, and absence of history alone is not either — a fresh account with a
/// profile is just a new viewer.</para>
/// </summary>
public static class FollowBotTrack
{
    /// <summary>
    /// Handles like <c>viewer80421933</c> or <c>xqcfan2837461</c>: a word followed by a long digit run,
    /// which is what bulk registration produces. Deliberately requires 6+ digits — <c>tom1994</c> is a
    /// birth year and <c>player123</c> is a person.
    /// </summary>
    private static readonly Regex GeneratedHandle = new(
        @"^[a-z_]{3,}\d{6,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>An account with no profile at all, younger than this, has nothing to show for itself.</summary>
    private const double FreshAccountHours = 48;

    /// <summary>Following and unfollowing this many times is inventory cycling, not indecision.</summary>
    private const int OscillationCycles = 3;

    /// <summary>
    /// Examine everyone who followed during a spike and return only those with their own evidence.
    /// Accounts with standing are excluded before anything else is considered (SD11).
    /// </summary>
    public static FollowBotBlockBatch Examine(IReadOnlyCollection<FollowCandidate> candidates)
    {
        List<FollowBotFinding> findings = [];

        foreach (FollowCandidate candidate in candidates)
        {
            if (TrustTierLadder.IsShieldedFromAutomatedAccountAction(candidate.Tier))
                continue;

            List<FollowBotIndicator> indicators = IndicatorsFor(candidate);
            if (indicators.Count > 0)
                findings.Add(new FollowBotFinding(candidate.AccountId, indicators));
        }

        return new FollowBotBlockBatch(findings, candidates.Count);
    }

    /// <summary>The per-account evidence, each item something the account itself produced.</summary>
    public static List<FollowBotIndicator> IndicatorsFor(FollowCandidate candidate)
    {
        List<FollowBotIndicator> indicators = [];

        if (candidate.IsOnKnownBotList)
            indicators.Add(FollowBotIndicator.KnownBotId);

        if (GeneratedHandle.IsMatch(candidate.Username))
            indicators.Add(FollowBotIndicator.GeneratedHandlePattern);

        // Both halves are required. A fresh account WITH a profile is a new viewer, and an empty
        // profile on an old account is just someone who never filled it in.
        if (candidate.AccountAgeHours < FreshAccountHours && !candidate.HasProfileContent)
            indicators.Add(FollowBotIndicator.ZeroHistoryFreshAccount);

        if (candidate.FollowUnfollowCycles >= OscillationCycles)
            indicators.Add(FollowBotIndicator.FollowUnfollowOscillation);

        return indicators;
    }
}
