// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One correlated cohort, persisted (spam-defense.md §L3.0).
///
/// <para>Stored rather than kept in memory because the verdict is <b>reversible for the life of its
/// window</b>: when regulars join a phrase and the cohort de-qualifies, the system has to know exactly
/// which accounts it actioned in order to undo them. A cohort held only in process memory loses that
/// list on every restart, and the people it actioned stay actioned.</para>
/// </summary>
public class SpamCampaignRecord : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The normalized text the cohort formed around.</summary>
    public string Skeleton { get; set; } = string.Empty;

    public CohortVerdict Verdict { get; set; }

    /// <summary>Everyone who posted the skeleton — the set that decided WHETHER this was a campaign.</summary>
    public int QualificationCount { get; set; }

    /// <summary>The qualification set minus everyone with standing — who could be acted on.</summary>
    public int ActionableCount { get; set; }

    /// <summary>How many were actually actioned, and therefore how many a reversal must restore.</summary>
    public int ActionedCount { get; set; }

    /// <summary>Share of the cohort with no positive standing. The number the verdict turned on.</summary>
    public double NoStandingShare { get; set; }

    /// <summary>
    /// Comma-separated platform ids this campaign actioned. Kept so a de-qualification can undo exactly
    /// those accounts and no others — a reversal that guesses is worse than none.
    /// </summary>
    public string ActionedAccountIds { get; set; } = string.Empty;

    /// <summary>
    /// Everyone who posted the skeleton, comma-separated. Stored rather than counted because SD9 needs
    /// MEMBERSHIP, not presence: an account may only be actioned if it actually posted the phrase, and
    /// a restart that reduced the cohort to a number would leave the engine unable to tell the two
    /// apart.
    /// </summary>
    public string MemberAccountIds { get; set; } = string.Empty;

    /// <summary>
    /// The subset with positive standing. Kept separately so the share can be recomputed exactly on
    /// reload, and so SD11 exclusion does not depend on re-resolving every member's tier.
    /// </summary>
    public string StandingAccountIds { get; set; } = string.Empty;

    /// <summary>When the cohort first qualified — the clock the action delay runs from.</summary>
    public DateTime? QualifiedAt { get; set; }

    /// <summary>
    /// A de-qualified cohort never re-qualifies within its window. Persisted because the latch is the
    /// whole of the one-way guarantee: reset by a restart, more strangers arriving would re-action
    /// people the regulars had already exonerated.
    /// </summary>
    public bool IsDequalified { get; set; }

    /// <summary>When this cohort stops accepting observations.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// False once any member with standing has been seen. Such a skeleton is never contributed to the
    /// network: a false signature propagated to every subscriber is the worst outcome available here.
    /// </summary>
    public bool MayContributeToNetwork { get; set; } = true;

    /// <summary>
    /// Set only once EVERY account in <see cref="ActionedAccountIds"/> was actually restored. A partial
    /// restore (some accounts unbanned, others still refusing) must never stamp this — the field means
    /// "nobody this campaign touched is still actioned", not "an attempt was made".
    /// </summary>
    public DateTime? ReversedAt { get; set; }

    /// <summary>Why it was reversed, in words an operator can read back later.</summary>
    public string? ReversalReason { get; set; }

    /// <summary>
    /// Who or what performed the reversal — an operator's user id, or the system actor id for the
    /// automatic de-qualification path (<c>SpamCampaignReversalExecutor.SystemActorId</c>). Required for
    /// an audit trail: a reversal nobody can attribute is one nobody can review.
    /// </summary>
    public string? ReversedByActorId { get; set; }

    /// <summary>How many of <see cref="ActionedAccountIds"/> were actually restored on the last attempt.</summary>
    public int RestoredAccountCount { get; set; }

    /// <summary>
    /// Comma-separated accounts the last restoration attempt could NOT restore — empty when the
    /// restoration was complete. Kept so a partial failure is never silent: an operator (or a later
    /// retry) can see exactly who is still actioned.
    /// </summary>
    public string RestorationFailedAccountIds { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }
}

/// <summary>
/// One account blocked by the follow-bot track, with the evidence that justified it
/// (spam-defense.md §L3.1 / SD9).
///
/// <para><see cref="Indicators"/> is required and non-empty by contract: per SD9 a block without the
/// account's own evidence must not be representable, and a stored block that cannot say why is exactly
/// that. The batch id makes a misread viral moment reversible in one action.</para>
/// </summary>
public class FollowBotBlock : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>Groups every block from one spike, so the whole sweep can be restored together.</summary>
    public Guid BatchId { get; set; }

    public string SubjectPlatformUserId { get; set; } = string.Empty;

    public string SubjectUsername { get; set; } = string.Empty;

    /// <summary>Comma-separated <see cref="FollowBotIndicator"/> names. Never empty.</summary>
    public string Indicators { get; set; } = string.Empty;

    /// <summary>How many accounts the spike window contained — the denominator for the sweep.</summary>
    public int BatchExamined { get; set; }

    /// <summary>Set when the operator restored this account.</summary>
    public DateTime? RestoredAt { get; set; }

    public DateTime BlockedAt { get; set; }
}
