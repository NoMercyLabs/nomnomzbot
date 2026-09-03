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

/// <summary>How sure the stack is, per SD1.</summary>
public enum SpamConfidence
{
    /// <summary>No content signal fired. Nothing happens — this is where ordinary chat lands.</summary>
    Zero = 0,

    /// <summary>A single weak signal. Flag for a human; no action.</summary>
    Low = 1,

    /// <summary>An unearned capability, or promo shape without a corpus hit. Delete + queue, never the account.</summary>
    Medium = 2,

    /// <summary>Cosmetic-abuse characters, corpus hit, confirmed cohort, malicious link.</summary>
    High = 3,
}

/// <summary>What the engine decided to do. Ordered so a stricter outcome is a larger value.</summary>
public enum SpamOutcome
{
    /// <summary>Nothing at all — not even a record beyond the routine trust counter.</summary>
    None = 0,

    /// <summary>Visible to moderators with the full explanation. No action against message or account.</summary>
    Flag = 1,

    /// <summary>Message removed and the deletion queued for review. Reversible; the account is untouched.</summary>
    DeleteAndQueue = 2,

    /// <summary>Delete plus an account action routed into the existing escalation ladder.</summary>
    DeleteAndEscalate = 3,
}

/// <summary>
/// One enforcement decision, carrying its own explanation (SD7). Nothing here is a bare verdict: a
/// moderator can always see what fired, what tier the viewer holds, and — when the engine chose NOT to
/// act — why not.
/// </summary>
/// <param name="Outcome">What will actually happen.</param>
/// <param name="WouldHaveBeen">
/// What would have happened with enforcement on. Equal to <paramref name="Outcome"/> outside dry run.
/// This is the whole point of §6.2: the operator reads a week of these before switching enforcement on,
/// and sees a wrongly-caught regular in a list instead of in an apology.
/// </param>
/// <param name="IsDryRun">True when the channel is still observing rather than acting.</param>
/// <param name="Reason">Why this outcome, in terms a moderator can act on.</param>
public sealed record SpamDecision(
    SpamOutcome Outcome,
    SpamOutcome WouldHaveBeen,
    bool IsDryRun,
    string Reason
)
{
    /// <summary>True when the engine looked and deliberately did nothing to the account.</summary>
    public bool TouchesAccount => Outcome == SpamOutcome.DeleteAndEscalate;
}

/// <summary>
/// L5 enforcement (spam-defense.md §L5) — turns a confidence into an outcome, under two ceilings that
/// are checked BEFORE the confidence is even consulted.
///
/// <para><b>Order matters and is the safety property.</b> Immunity (SD8) and the standing ceiling (SD11)
/// are applied first, so there is no path by which a high confidence "wins" against them. Writing it the
/// other way round — decide, then soften — is how an engine ends up with a window in which it hurts
/// someone, and §9 requires this to ship in the same slice as the scorer for exactly that reason.</para>
///
/// <para><b>Dry run is the default</b> (§6.2). A channel observes for its first 7 days: every layer
/// evaluates, every decision is recorded with its explanation, and nothing is acted on. Dry run is also
/// available permanently, and is the recommended state for a channel that only wants visibility.</para>
/// </summary>
public static class SpamEnforcement
{
    /// <summary>
    /// Decide what happens to a message. <paramref name="dryRun"/> defaults to TRUE: a caller that has
    /// not explicitly opted into enforcement observes rather than acts, so forgetting to pass it cannot
    /// silently start punishing people.
    /// </summary>
    public static SpamDecision Decide(
        SpamConfidence confidence,
        SpamTrustTier tier,
        bool dryRun = true
    )
    {
        // SD8, first and unconditionally. Not a lower score — a short-circuit.
        if (TrustTierLadder.IsImmune(tier))
            return Build(
                SpamOutcome.Flag,
                dryRun,
                confidence == SpamConfidence.Zero
                    ? "Established viewer; nothing fired."
                    : $"Established viewer — {Describe(confidence)} flagged for a human. "
                        + "An established regular is never actioned automatically."
            );

        // SD11 ceiling: standing means the engine may delete and flag, never touch the account.
        if (TrustTierLadder.IsShieldedFromAutomatedAccountAction(tier))
        {
            SpamOutcome shielded = confidence switch
            {
                SpamConfidence.Zero => SpamOutcome.None,
                SpamConfidence.Low => SpamOutcome.Flag,
                _ => SpamOutcome.DeleteAndQueue,
            };
            return Build(
                shielded,
                dryRun,
                shielded == SpamOutcome.None
                    ? "Nothing fired."
                    : $"{Describe(confidence)} — viewer has standing, so the message is handled "
                        + "but the account is not. A human decides anything further."
            );
        }

        SpamOutcome outcome = confidence switch
        {
            // SD10 lands here: every silent, new or odd-looking account saying something ordinary.
            SpamConfidence.Zero => SpamOutcome.None,
            SpamConfidence.Low => SpamOutcome.Flag,
            // Medium NEVER touches the account, at any tier. Restoring the message credits trust.
            SpamConfidence.Medium => SpamOutcome.DeleteAndQueue,
            SpamConfidence.High => SpamOutcome.DeleteAndEscalate,
            _ => SpamOutcome.None,
        };

        return Build(outcome, dryRun, ReasonFor(confidence, outcome));
    }

    /// <summary>
    /// In dry run the outcome becomes <see cref="SpamOutcome.None"/> while
    /// <see cref="SpamDecision.WouldHaveBeen"/> keeps the real verdict, so the dashboard can show exactly
    /// what would have happened without anything happening.
    /// </summary>
    private static SpamDecision Build(SpamOutcome outcome, bool dryRun, string reason) =>
        new(dryRun ? SpamOutcome.None : outcome, outcome, dryRun, reason);

    private static string ReasonFor(SpamConfidence confidence, SpamOutcome outcome) =>
        outcome switch
        {
            SpamOutcome.None => "Nothing fired.",
            SpamOutcome.Flag => $"{Describe(confidence)} — flagged for review, no action taken.",
            SpamOutcome.DeleteAndQueue =>
                $"{Describe(confidence)} — message removed and queued for review. The account is "
                    + "untouched; restoring the message credits the sender's trust.",
            _ => $"{Describe(confidence)} — message removed and routed to the escalation ladder.",
        };

    private static string Describe(SpamConfidence confidence) =>
        confidence switch
        {
            SpamConfidence.High => "High confidence",
            SpamConfidence.Medium => "Medium confidence",
            SpamConfidence.Low => "A single weak signal",
            _ => "No signal",
        };
}
