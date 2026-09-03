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

/// <summary>What a cohort of accounts posting the same skeleton has been judged to be.</summary>
public enum CohortVerdict
{
    /// <summary>Not enough accounts yet, or the share of strangers is below the bar. No action.</summary>
    Watching,

    /// <summary>A coordinated campaign. Members may be actioned — each on their own evidence.</summary>
    Campaign,

    /// <summary>
    /// Regulars are posting it, so it is community behaviour: an in-joke, a raid greeting, a copypasta.
    /// Recorded for the mod feed, never actioned, and never contributed to the corpus.
    /// </summary>
    CommunityPattern,
}

/// <summary>Per-channel correlation thresholds (spam-defense.md §L3.0.1). Operator-editable.</summary>
public sealed record CohortThresholds
{
    /// <summary>Share of members with NO positive standing needed to call a cohort a campaign.</summary>
    public double QualifyNoStandingShare { get; init; } = 0.80;

    /// <summary>
    /// The share a qualified cohort must fall below to be de-qualified. Deliberately lower than
    /// <see cref="QualifyNoStandingShare"/>: the gap is a hysteresis band, so a cohort hovering on the
    /// line cannot flap between actioning and reversing.
    /// </summary>
    public double DequalifyNoStandingShare { get; init; } = 0.65;

    /// <summary>Below this many distinct accounts, nothing is a campaign however identical the text.</summary>
    public int MinimumDistinctAccounts { get; init; } = 5;

    /// <summary>Window from the first match, extended by each new match.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Hard cap on the window measured from the first match, however many matches arrive.</summary>
    public TimeSpan MaximumWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long to wait after qualification before acting. The real trade-off, and the operator's to
    /// make: longer means exoneration almost always beats the ban, at the cost of visible spam.
    /// </summary>
    public TimeSpan ActionDelay { get; init; } = TimeSpan.FromSeconds(8);
}

/// <summary>
/// What must be undone when a cohort de-qualifies. Reversal is automatic and not a suggestion —
/// waiting for a moderator to notice means somebody stays banned through the rest of the stream for
/// laughing along, which is the failure SD0 exists to prevent.
/// </summary>
/// <param name="AccountsToRestore">Every account this campaign actioned. Untimeout, unban.</param>
/// <param name="RemoveSkeletonFromCorpus">The premise for adding it no longer holds.</param>
/// <param name="OperatorMessage">Why, in words an operator can act on.</param>
public sealed record CampaignReversal(
    IReadOnlyCollection<string> AccountsToRestore,
    bool RemoveSkeletonFromCorpus,
    string OperatorMessage
);

/// <summary>
/// One cohort of accounts posting the same skeleton inside a window (spam-defense.md §L3.0, §L3.0.1).
///
/// <para><b>A campaign is not defined by the message — it is defined by who is sending it.</b> A beloved
/// community copypasta and a coordinated spam campaign are the same shape: many accounts, near-identical
/// text, tight window. The only thing that separates them is whether the people posting it have standing
/// in the room. That is why qualification counts <i>everyone</i> who posted the skeleton while action
/// touches only those without standing: a regular counts toward the verdict — that is exactly how they
/// vouch for the phrase — and can never be punished by it.</para>
///
/// <para><b>The verdict can only ever soften.</b> Strangers start, regulars join: twenty no-standing
/// accounts post a phrase, it qualifies, actions fire, and then the regulars pile in because it was a
/// joke. A cohort that de-qualifies never re-qualifies within its window, and everything it did is
/// undone.</para>
/// </summary>
public sealed class CampaignCohort
{
    private readonly Dictionary<string, bool> _membersHaveStanding = [];
    private readonly HashSet<string> _actioned = [];
    private readonly CohortThresholds _thresholds;
    private readonly DateTimeOffset _firstMatchAt;
    private DateTimeOffset _expiresAt;
    private DateTimeOffset? _qualifiedAt;

    public CampaignCohort(
        string skeleton,
        DateTimeOffset firstMatchAt,
        CohortThresholds? thresholds = null
    )
    {
        Skeleton = skeleton;
        _thresholds = thresholds ?? new CohortThresholds();
        _firstMatchAt = firstMatchAt;
        _expiresAt = firstMatchAt + _thresholds.Window;
    }

    /// <summary>The normalized text this cohort formed around.</summary>
    public string Skeleton { get; }

    /// <summary>The current verdict. Starts <see cref="CohortVerdict.Watching"/>.</summary>
    public CohortVerdict Verdict { get; private set; } = CohortVerdict.Watching;

    /// <summary>
    /// True once the cohort has been de-qualified. A latch, never cleared: the verdict is one-way, so a
    /// cohort exonerated by its regulars cannot be re-qualified by more strangers arriving.
    /// </summary>
    public bool IsDequalified { get; private set; }

    /// <summary>
    /// True if any member with standing has EVER been seen, even one later dropped from the share. Such
    /// a skeleton is never contributed to the network: a false signature propagated to every subscriber
    /// is the worst outcome this system can produce.
    /// </summary>
    public bool MayContributeToNetwork { get; private set; } = true;

    /// <summary>When this cohort stops accepting observations.</summary>
    public DateTimeOffset ExpiresAt => _expiresAt;

    /// <summary>Everyone who posted the skeleton — the set that decides WHETHER this is a campaign.</summary>
    public IReadOnlyCollection<string> QualificationSet => _membersHaveStanding.Keys;

    /// <summary>
    /// The qualification set minus everyone with standing — the set that decides WHO may be acted on.
    /// Conflating the two is the classic error this design exists to avoid.
    /// </summary>
    public IReadOnlyCollection<string> ActionSet =>
        _membersHaveStanding.Where(m => !m.Value).Select(m => m.Key).ToList();

    /// <summary>Share of members with no positive standing. The number the verdict turns on.</summary>
    public double NoStandingShare =>
        _membersHaveStanding.Count == 0
            ? 0
            : (double)_membersHaveStanding.Count(m => !m.Value) / _membersHaveStanding.Count;

    /// <summary>
    /// Record one account posting this skeleton, and re-judge. Observations after the window closes are
    /// ignored — a cohort is a statement about a moment, not a permanent list.
    /// </summary>
    public CohortVerdict Observe(string accountId, SpamTrustTier tier, DateTimeOffset at)
    {
        if (at > _expiresAt)
            return Verdict;

        bool hasStanding = TrustTierLadder.IsShieldedFromAutomatedAccountAction(tier);
        _membersHaveStanding[accountId] = hasStanding;
        if (hasStanding)
            MayContributeToNetwork = false;

        // Each new match extends the window, but never past the cap measured from the FIRST match.
        DateTimeOffset extended = at + _thresholds.Window;
        DateTimeOffset ceiling = _firstMatchAt + _thresholds.MaximumWindow;
        _expiresAt = extended > ceiling ? ceiling : extended;

        Rejudge(at);
        return Verdict;
    }

    private void Rejudge(DateTimeOffset at)
    {
        // One-way. A de-qualified cohort stays de-qualified for the life of its window.
        if (IsDequalified)
            return;

        if (Verdict == CohortVerdict.Campaign)
        {
            if (NoStandingShare < _thresholds.DequalifyNoStandingShare)
            {
                IsDequalified = true;
                Verdict = CohortVerdict.CommunityPattern;
            }
            return;
        }

        bool qualifies =
            _membersHaveStanding.Count >= _thresholds.MinimumDistinctAccounts
            && NoStandingShare >= _thresholds.QualifyNoStandingShare;

        if (qualifies)
        {
            Verdict = CohortVerdict.Campaign;
            _qualifiedAt ??= at;
        }
    }

    /// <summary>
    /// Whether this account may be acted on right now. Every clause is a separate guard, because each
    /// one alone has been the whole bug in somebody's moderation system:
    /// the cohort must be a campaign, the account must have actually posted the skeleton (presence in
    /// the window is not membership — SD9), the account must have no standing (SD11), and the action
    /// delay must have elapsed so a regular has had a chance to exonerate the phrase.
    /// </summary>
    public bool MayActOn(string accountId, DateTimeOffset now)
    {
        if (Verdict != CohortVerdict.Campaign || IsDequalified)
            return false;
        if (!_membersHaveStanding.TryGetValue(accountId, out bool hasStanding))
            return false;
        if (hasStanding)
            return false;

        return _qualifiedAt is not null && now >= _qualifiedAt.Value + _thresholds.ActionDelay;
    }

    /// <summary>Remember that this campaign actioned an account, so reversal knows what to undo.</summary>
    public void RecordAction(string accountId) => _actioned.Add(accountId);

    /// <summary>Accounts this campaign has actioned, for persistence and for reversal.</summary>
    public IReadOnlyCollection<string> ActionedAccounts => _actioned;

    /// <summary>Members with positive standing, kept separately so a reload need not re-resolve tiers.</summary>
    public IReadOnlyCollection<string> StandingMembers =>
        _membersHaveStanding.Where(m => m.Value).Select(m => m.Key).ToList();

    /// <summary>When the cohort first qualified — the clock the action delay runs from.</summary>
    public DateTimeOffset? QualifiedAt => _qualifiedAt;

    /// <summary>
    /// Rebuild a cohort from stored state.
    ///
    /// <para>Exists so correlation survives a restart with its guarantees intact. The de-qualification
    /// latch and the qualified-at clock are restored rather than recomputed: reset by a restart, more
    /// strangers arriving would re-action people the regulars had already exonerated, and the action
    /// delay would start again from zero every time the process bounced.</para>
    /// </summary>
    public static CampaignCohort Rehydrate(
        string skeleton,
        DateTimeOffset firstMatchAt,
        DateTimeOffset expiresAt,
        IEnumerable<string> members,
        IEnumerable<string> standingMembers,
        IEnumerable<string> actioned,
        CohortVerdict verdict,
        bool isDequalified,
        DateTimeOffset? qualifiedAt,
        bool mayContributeToNetwork,
        CohortThresholds? thresholds = null
    )
    {
        CampaignCohort cohort = new(skeleton, firstMatchAt, thresholds);
        HashSet<string> standing = [.. standingMembers];

        foreach (string member in members)
            cohort._membersHaveStanding[member] = standing.Contains(member);
        foreach (string account in actioned)
            cohort._actioned.Add(account);

        cohort._expiresAt = expiresAt;
        cohort.Verdict = verdict;
        cohort.IsDequalified = isDequalified;
        cohort._qualifiedAt = qualifiedAt;
        cohort.MayContributeToNetwork = mayContributeToNetwork;
        return cohort;
    }

    /// <summary>
    /// What to undo, or <c>null</c> when the cohort was never de-qualified. Returns a reversal even when
    /// nothing was actioned — the skeleton still has to come back out of the corpus, and the operator
    /// still needs to be told the pattern was misread.
    /// </summary>
    public CampaignReversal? BuildReversal()
    {
        if (!IsDequalified)
            return null;

        int regulars = _membersHaveStanding.Count(m => m.Value);
        return new CampaignReversal(
            _actioned.ToList(),
            RemoveSkeletonFromCorpus: true,
            $"{regulars} regulars joined this pattern; it is not spam."
        );
    }
}
