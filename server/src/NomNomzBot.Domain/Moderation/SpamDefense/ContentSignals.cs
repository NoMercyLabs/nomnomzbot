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

/// <summary>What fired on a message (spam-defense.md §L2). Every signal is named so it can be explained.</summary>
public enum ContentSignal
{
    /// <summary>Combining marks or invisibles were present. SD2 high-confidence on its own.</summary>
    CosmeticAbuse,

    /// <summary>A single token mixed two scripts (`ѕtream`). Near-zero false-positive rate.</summary>
    IntraTokenScriptMixing,

    /// <summary>Exact skeleton hit against the corpus.</summary>
    CorpusMatch,

    /// <summary>Close enough to a corpus entry to be the next mutation of a known campaign.</summary>
    NearDuplicate,

    /// <summary>A link to a domain on the deny/malicious set.</summary>
    MaliciousLink,

    /// <summary>Contact handles, price/offer vocabulary, imperative CTAs.</summary>
    PromoShape,
}

/// <summary>The signals a message produced, and the confidence they fuse to.</summary>
public sealed record ContentEvaluation(
    IReadOnlyList<ContentSignal> Signals,
    SpamConfidence Confidence
)
{
    public bool Fired => Signals.Count > 0;
}

/// <summary>The corpus and per-channel policy L2 evaluates against.</summary>
public sealed record ContentPolicy
{
    /// <summary>Known spam skeletons — local plus any subscribed network signatures.</summary>
    public IReadOnlyCollection<string> CorpusSkeletons { get; init; } = [];

    /// <summary>Domains known malicious, plus the channel's own deny list.</summary>
    public IReadOnlyCollection<string> DeniedDomains { get; init; } = [];

    /// <summary>Domains the channel explicitly permits, checked before the deny set.</summary>
    public IReadOnlyCollection<string> AllowedDomains { get; init; } = [];

    /// <summary>
    /// Shingle-overlap similarity (0..1) at which a message counts as a near-duplicate of a corpus
    /// entry. 0 disables near-duplicate matching entirely, leaving exact corpus matching in place.
    ///
    /// <para>0.6 is chosen with a wide margin on both sides: measured against the seed corpus, real
    /// mutations of a campaign score 0.73 and above, while unrelated chat scores 0.00–0.16 — including
    /// messages that share a whole word with the campaign.</para>
    /// </summary>
    public double NearDuplicateSimilarity { get; init; } = 0.6;

    /// <summary>
    /// Skeletons shorter than this are never corpus-matched. Guards the false positives a two-letter
    /// skeleton would otherwise produce against ordinary chat.
    /// </summary>
    public int MinimumSkeletonLength { get; init; } = 8;

    /// <summary>
    /// Skeletons this channel's own regulars post (an in-joke, a copypasta). Marked benign locally and
    /// NEVER contributed to the network — one channel's catchphrase is another's spam.
    /// </summary>
    public IReadOnlyCollection<string> ChannelBenignSkeletons { get; init; } = [];
}

/// <summary>
/// L2 content signals (spam-defense.md §L2), evaluated against the L0 skeleton.
///
/// <para>This layer answers only "what did the message do?". It never looks at who sent it — account
/// shape is L1's coefficient and standing is L4's ceiling, both applied elsewhere. Keeping them apart is
/// what makes SD10 enforceable: a content score of zero cannot be rescued into an action by anything
/// known about the account.</para>
/// </summary>
public static class ContentSignals
{
    /// <summary>Contact handles and storefronts that carry the promo shape.</summary>
    private static readonly Regex ContactHandle = new(
        @"(t\s*\.?\s*me/|discord\s*\.?\s*gg/|telegram|whatsapp|cash\s*app|@[a-z0-9_]{3,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>Offer vocabulary — the words a promo needs and ordinary chat rarely stacks.</summary>
    private static readonly Regex OfferVocabulary = new(
        @"(cheap|promo|discount|free\s*follow|buy\s*(followers|viewers)|best\s*viewers|"
            + @"grow\s*your|boost\s*your|earn\s*\$|make\s*money|crypto|giveaway\s*winner)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>Anything shaped like a domain, read from the SKELETON so `t.me∕x` is still seen.</summary>
    private static readonly Regex DomainLike = new(
        @"([a-z0-9-]+(?:\.[a-z0-9-]+)+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// Evaluate a normalized message. <paramref name="rawText"/> is the original, used only for
    /// link/promo shapes that punctuation carries — the skeleton has had punctuation stripped.
    /// </summary>
    public static ContentEvaluation Evaluate(
        NormalizedMessage message,
        string rawText,
        ContentPolicy? policy = null
    )
    {
        ContentPolicy p = policy ?? new ContentPolicy();
        List<ContentSignal> signals = [];

        // A skeleton this channel's regulars post is benign HERE. Checked first, so a community
        // catchphrase never accumulates signals in the channel that loves it.
        if (p.ChannelBenignSkeletons.Contains(message.Skeleton))
            return new ContentEvaluation([], SpamConfidence.Zero);

        if (message.StrippedCosmeticAbuse)
            signals.Add(ContentSignal.CosmeticAbuse);

        if (message.MixedScriptTokens.Count > 0)
            signals.Add(ContentSignal.IntraTokenScriptMixing);

        bool longEnoughToMatch = message.Skeleton.Length >= p.MinimumSkeletonLength;

        if (longEnoughToMatch && p.CorpusSkeletons.Contains(message.Skeleton))
            signals.Add(ContentSignal.CorpusMatch);
        else if (longEnoughToMatch && IsNearDuplicate(message.Skeleton, p))
            signals.Add(ContentSignal.NearDuplicate);

        if (HasDeniedLink(rawText, message.Skeleton, p))
            signals.Add(ContentSignal.MaliciousLink);

        if (HasPromoShape(rawText))
            signals.Add(ContentSignal.PromoShape);

        return new ContentEvaluation(signals, Fuse(signals));
    }

    /// <summary>
    /// SD1's confidence bands. High is reserved for signals that are near-unambiguous on their own;
    /// promo shape WITHOUT a corpus hit is explicitly Medium, because "cheap" and "@handle" appear in
    /// ordinary chat and a delete-and-queue is recoverable where an account action is not.
    /// </summary>
    private static SpamConfidence Fuse(IReadOnlyList<ContentSignal> signals)
    {
        if (signals.Count == 0)
            return SpamConfidence.Zero;

        bool high = signals.Any(s =>
            s
                is ContentSignal.CosmeticAbuse
                    or ContentSignal.CorpusMatch
                    or ContentSignal.MaliciousLink
        );
        if (high)
            return SpamConfidence.High;

        // Two independent weak signals corroborate into Medium; one alone stays Low and only flags.
        return signals.Count >= 2 ? SpamConfidence.Medium : SpamConfidence.Low;
    }

    /// <summary>
    /// Catches the next mutation of a known campaign before anyone reports it: the spammer changes two
    /// characters and re-sends, and exact corpus matching alone would let it straight through.
    /// </summary>
    private static bool IsNearDuplicate(string skeleton, ContentPolicy policy)
    {
        if (policy.NearDuplicateSimilarity <= 0 || policy.CorpusSkeletons.Count == 0)
            return false;

        HashSet<string> shingles = Shingles(skeleton);
        foreach (string entry in policy.CorpusSkeletons)
        {
            if (entry.Length < policy.MinimumSkeletonLength)
                continue;
            if (Similarity(shingles, Shingles(entry)) >= policy.NearDuplicateSimilarity)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Jaccard similarity over character 4-shingles: the share of 4-character runs the two strings have
    /// in common.
    ///
    /// <para><b>Why not SimHash</b>, which §L2 names. SimHash compares 64-bit fingerprints by Hamming
    /// distance and is built for document-length text, where near-duplicates cluster within a handful of
    /// bits. Chat messages are ~30 characters, which is far too little input to settle those 64 bits:
    /// measured on the seed corpus, a two-character mutation of a campaign landed 13 bits away while two
    /// entirely DIFFERENT campaigns landed 19 apart — overlapping ranges, and nothing like the "≤ 3" the
    /// spec assumes. Comparing the shingle sets directly removes the fingerprint step that was throwing
    /// the information away, and separates the same cases 0.73 against 0.16. The spec's intent — catch
    /// the next mutation, do not catch ordinary chat — is what is implemented; only the instrument
    /// differs, and it differs because the named one does not work at this length.</para>
    /// </summary>
    public static double Similarity(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        int shared = left.Count(right.Contains);
        int union = left.Count + right.Count - shared;
        return union == 0 ? 0 : (double)shared / union;
    }

    /// <summary>Every 4-character run in <paramref name="text"/>.</summary>
    public static HashSet<string> Shingles(string text)
    {
        HashSet<string> shingles = [];
        for (int i = 0; i + 4 <= text.Length; i++)
            shingles.Add(text.Substring(i, 4));
        return shingles;
    }

    /// <summary>
    /// Links are read from BOTH the raw text and the skeleton: the skeleton survives unicode-dot and
    /// spaced-out evasion (`t.me∕x`, `bit␣ly/x`), while the raw text keeps the dots an ordinary link has.
    /// The channel's allow list is checked first, so a streamer's own domains are never flagged.
    /// </summary>
    private static bool HasDeniedLink(string rawText, string skeleton, ContentPolicy policy)
    {
        if (policy.DeniedDomains.Count == 0)
            return false;

        foreach (Match match in DomainLike.Matches(rawText))
        {
            string domain = match.Value.ToLowerInvariant();
            if (
                policy.AllowedDomains.Any(a =>
                    domain.Contains(a, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            if (
                policy.DeniedDomains.Any(d =>
                    domain.Contains(d, StringComparison.OrdinalIgnoreCase)
                )
            )
                return true;
        }

        // The skeleton has lost its dots, so a denied domain is matched as a contiguous run.
        foreach (string denied in policy.DeniedDomains)
        {
            string collapsed = denied.Replace(".", string.Empty).ToLowerInvariant();
            if (collapsed.Length >= 5 && skeleton.Contains(collapsed, StringComparison.Ordinal))
            {
                bool allowed = policy.AllowedDomains.Any(a =>
                    skeleton.Contains(
                        a.Replace(".", string.Empty),
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                if (!allowed)
                    return true;
            }
        }

        return false;
    }

    private static bool HasPromoShape(string rawText) =>
        ContactHandle.IsMatch(rawText) && OfferVocabulary.IsMatch(rawText);
}
