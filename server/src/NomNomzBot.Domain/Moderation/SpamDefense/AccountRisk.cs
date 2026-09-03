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

/// <summary>A single account-shape observation, recorded whether or not it moves the coefficient.</summary>
public enum AccountRiskMark
{
    /// <summary>Account created under 7 days ago.</summary>
    AccountUnder7Days,

    /// <summary>Account created under 30 days ago.</summary>
    AccountUnder30Days,

    /// <summary>Account created under 6 months ago.</summary>
    AccountUnder6Months,

    /// <summary>Not following, or following for under 24 hours.</summary>
    NotFollowingOrBrandNewFollow,

    /// <summary>No avatar, empty bio, no streams — the shape of a throwaway.</summary>
    DefaultProfile,

    /// <summary>Username looks machine-generated: a word plus 4–8 digits, or high entropy.</summary>
    GeneratedHandlePattern,

    /// <summary>
    /// Their first message ever in this channel. Recorded, shown to the moderator, and worth ×1.0 —
    /// see <see cref="AccountRisk"/> for why this one can never weigh against anybody.
    /// </summary>
    FirstMessageInChannel,

    /// <summary>Zero chat history anywhere on this instance. Also pinned at ×1.0.</summary>
    NoChatHistoryOnInstance,
}

/// <summary>
/// The account-risk coefficient and the marks behind it (spam-defense.md §L1).
/// </summary>
/// <param name="Coefficient">
/// The multiplier applied to a content-signal score. 1.0 means "nothing about this account makes a
/// suspicious message more suspicious".
/// </param>
/// <param name="Marks">
/// Every observation made, including the two inert ones. A moderator must be able to see that the system
/// looked at an account and chose not to weigh something (SD7) — an unexplained score is a black box.
/// </param>
/// <param name="IsSemiTrusted">
/// Positive standing was found (§L1.2), so the coefficient is forced to 1.0 and the enforcement ceiling
/// for this viewer is delete-and-flag. No automated ban or timeout, ever, at any score.
/// </param>
public sealed record AccountRiskAssessment(
    double Coefficient,
    IReadOnlyList<AccountRiskMark> Marks,
    bool IsSemiTrusted
);

/// <summary>
/// What we know about an account when a message arrives. Every field is optional-by-default so a caller
/// that has not resolved a signal yet passes nothing rather than guessing a value that would weigh
/// against the viewer.
/// </summary>
public sealed record AccountFacts
{
    public double AccountAgeDays { get; init; } = double.MaxValue;
    public bool IsFollowing { get; init; } = true;
    public double FollowAgeHours { get; init; } = double.MaxValue;
    public bool HasAvatar { get; init; } = true;
    public bool HasBio { get; init; } = true;
    public bool HasStreamed { get; init; } = true;
    public string Username { get; init; } = string.Empty;
    public bool IsFirstMessageInChannel { get; init; }
    public bool HasChatHistoryOnInstance { get; init; } = true;

    // ─── Positive standing (§L1.2), checked BEFORE risk ───
    public bool IsModeratorAnywhere { get; init; }
    public bool IsVipAnywhere { get; init; }
    public bool IsSubscriberAnywhere { get; init; }
    public double WatchTimeHoursThisChannel { get; init; }
    public double WatchTimeHoursInstanceWide { get; init; }
    public bool IsPartnerOrAffiliate { get; init; }
}

/// <summary>
/// L1 of the spam-defence stack (spam-defense.md §L1) — how hard a SUSPICIOUS message is judged, based on
/// what the account looks like.
///
/// <para><b>It multiplies; it never adds (SD10, §L1.1).</b> The final score is
/// <c>ContentSignalScore × Coefficient</c>, so a content signal of zero times any coefficient is still
/// zero. An account carrying every risk mark that says something ordinary scores nothing and is not
/// evaluated further. Nobody is ever actioned for <i>what they are</i> — only for <i>what they said</i>.
/// There is deliberately no additive path from this class into a score, and adding one would break the
/// single property the whole layer exists to guarantee.</para>
///
/// <para><b>The two silence marks are inert on purpose.</b> "First message ever here" and "no history
/// anywhere" describe a lurker finally speaking — the most sympathetic person in the channel, and under
/// any additive scheme the one who stacks two penalties for the crime of having been quiet. They are
/// recorded and shown, and they move the coefficient by nothing. A ten-year lurker's first word is judged
/// exactly like a regular's thousandth.</para>
/// </summary>
public static class AccountRisk
{
    private const double AccountUnder7DaysMultiplier = 1.6;
    private const double AccountUnder30DaysMultiplier = 1.3;
    private const double AccountUnder6MonthsMultiplier = 1.1;
    private const double NotFollowingMultiplier = 1.2;
    private const double DefaultProfileMultiplier = 1.15;
    private const double GeneratedHandleMultiplier = 1.4;

    private const double SemiTrustedWatchHoursThisChannel = 10.0;
    private const double SemiTrustedWatchHoursInstanceWide = 25.0;

    /// <summary>A word followed by 4–8 digits — the shape auto-generated handles take.</summary>
    private static readonly System.Text.RegularExpressions.Regex GeneratedHandle = new(
        @"^[A-Za-z]{2,}\d{4,8}$",
        System.Text.RegularExpressions.RegexOptions.Compiled
    );

    /// <summary>
    /// Assess an account. Standing is evaluated FIRST and wins: a viewer with positive standing gets a
    /// coefficient of exactly 1.0 no matter how many risk marks their account shape carries, because no
    /// accumulation of suspicion is allowed to reach someone who has already shown they are real.
    /// </summary>
    public static AccountRiskAssessment Assess(AccountFacts facts)
    {
        List<AccountRiskMark> marks = CollectMarks(facts);

        if (HasPositiveStanding(facts))
            return new AccountRiskAssessment(1.0, marks, IsSemiTrusted: true);

        // Partner/affiliate and long-lived genuine accounts are not Semi-Trusted, but their shape stops
        // counting against them: the coefficient is pinned at 1.0 (§L1.2).
        if (facts.IsPartnerOrAffiliate || IsEstablishedGenuineAccount(facts))
            return new AccountRiskAssessment(1.0, marks, IsSemiTrusted: false);

        double coefficient = 1.0;
        foreach (AccountRiskMark mark in marks)
            coefficient *= MultiplierFor(mark);

        return new AccountRiskAssessment(coefficient, marks, IsSemiTrusted: false);
    }

    /// <summary>
    /// The multiplier a mark contributes. The two silence marks return exactly 1.0 — they are listed
    /// here rather than omitted so that adding a weight to them is a visible edit to this table, not an
    /// accident somewhere else.
    /// </summary>
    private static double MultiplierFor(AccountRiskMark mark) =>
        mark switch
        {
            AccountRiskMark.AccountUnder7Days => AccountUnder7DaysMultiplier,
            AccountRiskMark.AccountUnder30Days => AccountUnder30DaysMultiplier,
            AccountRiskMark.AccountUnder6Months => AccountUnder6MonthsMultiplier,
            AccountRiskMark.NotFollowingOrBrandNewFollow => NotFollowingMultiplier,
            AccountRiskMark.DefaultProfile => DefaultProfileMultiplier,
            AccountRiskMark.GeneratedHandlePattern => GeneratedHandleMultiplier,
            AccountRiskMark.FirstMessageInChannel => 1.0, // silence is never evidence
            AccountRiskMark.NoChatHistoryOnInstance => 1.0, // silence is never evidence
            _ => 1.0,
        };

    private static List<AccountRiskMark> CollectMarks(AccountFacts facts)
    {
        List<AccountRiskMark> marks = [];

        // Age bands do not stack — an account 3 days old is "under 7 days", not all three bands at once.
        if (facts.AccountAgeDays < 7)
            marks.Add(AccountRiskMark.AccountUnder7Days);
        else if (facts.AccountAgeDays < 30)
            marks.Add(AccountRiskMark.AccountUnder30Days);
        else if (facts.AccountAgeDays < 182)
            marks.Add(AccountRiskMark.AccountUnder6Months);

        if (!facts.IsFollowing || facts.FollowAgeHours < 24)
            marks.Add(AccountRiskMark.NotFollowingOrBrandNewFollow);

        if (!facts.HasAvatar && !facts.HasBio && !facts.HasStreamed)
            marks.Add(AccountRiskMark.DefaultProfile);

        if (!string.IsNullOrEmpty(facts.Username) && GeneratedHandle.IsMatch(facts.Username))
            marks.Add(AccountRiskMark.GeneratedHandlePattern);

        if (facts.IsFirstMessageInChannel)
            marks.Add(AccountRiskMark.FirstMessageInChannel);

        if (!facts.HasChatHistoryOnInstance)
            marks.Add(AccountRiskMark.NoChatHistoryOnInstance);

        return marks;
    }

    /// <summary>
    /// §L1.2. Watch time is the strongest signal we own: no public list has it, and a bot farm would have
    /// to genuinely watch to fake it. It is what lets standing reach a viewer who has never typed a word.
    /// </summary>
    private static bool HasPositiveStanding(AccountFacts facts) =>
        facts.IsModeratorAnywhere
        || facts.IsVipAnywhere
        || facts.IsSubscriberAnywhere
        || facts.WatchTimeHoursThisChannel >= SemiTrustedWatchHoursThisChannel
        || facts.WatchTimeHoursInstanceWide >= SemiTrustedWatchHoursInstanceWide;

    /// <summary>An account ≥ 2 years old WITH genuine activity — age alone is not enough.</summary>
    private static bool IsEstablishedGenuineAccount(AccountFacts facts) =>
        facts.AccountAgeDays >= 730
        && (facts.HasStreamed || facts.IsFollowing || facts.HasChatHistoryOnInstance);
}
