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

/// <summary>
/// The capability floor (spam-defense.md §L4): which things a message did that its sender's tier has not
/// earned yet, and what that is worth.
///
/// <para><b>It lifts a verdict; it never creates one on its own account.</b> An unearned capability raises
/// confidence to Medium (delete and queue, reversible, the account untouched) and no further, so a link
/// from a newcomer can never be more than a recoverable deletion. An Established viewer never reaches
/// this check at all (SD8) — the caller short-circuits before asking.</para>
///
/// <para>Detection is deliberately conservative: a false "link" deletes a real viewer's message, so the
/// pattern wants a scheme, <c>www.</c>, or a domain on a bounded list of top-level domains rather than
/// anything with a dot in it ("e.g.", "v1.2" and "end of sentence.Next" are chat, not links).</para>
/// </summary>
public static class CapabilityGate
{
    /// <summary>A scheme, a <c>www.</c> host, or a domain whose last label is a known top-level domain.</summary>
    private static readonly Regex LinkLike = new(
        @"(https?://\S+|\bwww\.\S+|\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|tv|gg|ly|me|xyz|ru|shop|store|site|club|biz|info|link|app|dev)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>The first code point past Latin Extended-B; everything letter-like beyond it is another script.</summary>
    private const char FirstNonLatinLetter = 'ɐ';

    private const char LatinExtendedAdditionalStart = 'Ḁ';
    private const char LatinExtendedAdditionalEnd = 'ỿ';

    /// <summary>
    /// The capability floors for a channel. The non-Latin gate is OFF by default (SD2), which lowers
    /// that one floor to Untrusted; switched on it is the shipped Regular floor.
    /// </summary>
    public static IReadOnlyDictionary<SpamCapability, SpamTrustTier> FloorsFor(
        bool nonLatinScriptGate
    )
    {
        Dictionary<SpamCapability, SpamTrustTier> floors = new(
            TrustTierLadder.DefaultCapabilityFloors
        );
        if (!nonLatinScriptGate)
            floors[SpamCapability.NonLatinScript] = SpamTrustTier.Untrusted;
        return floors;
    }

    /// <summary>The capabilities this message used. Only the ones the engine can read from text are reported.</summary>
    public static IReadOnlyList<SpamCapability> Detect(string rawText)
    {
        List<SpamCapability> used = [];

        if (LinkLike.IsMatch(rawText))
            used.Add(SpamCapability.PostLink);

        if (IsWrittenInAnotherScript(rawText))
            used.Add(SpamCapability.NonLatinScript);

        return used;
    }

    /// <summary>The used capabilities that <paramref name="tier"/> has not earned under this channel's floors.</summary>
    public static IReadOnlyList<SpamCapability> Unearned(
        string rawText,
        SpamTrustTier tier,
        bool nonLatinScriptGate
    )
    {
        IReadOnlyDictionary<SpamCapability, SpamTrustTier> floors = FloorsFor(nonLatinScriptGate);
        return
        [
            .. Detect(rawText)
                .Where(capability => !TrustTierLadder.Allows(tier, capability, floors)),
        ];
    }

    /// <summary>
    /// Raise to Medium when something is unearned. Never past what the content said on its own: High stays
    /// High, and the floor never lowers anything.
    /// </summary>
    public static SpamConfidence RaiseConfidence(
        SpamConfidence contentConfidence,
        IReadOnlyList<SpamCapability> unearned
    ) =>
        unearned.Count > 0 && contentConfidence < SpamConfidence.Medium
            ? SpamConfidence.Medium
            : contentConfidence;

    /// <summary>What a moderator reads on the detection: each unearned capability and the tier that earns it.</summary>
    public static string Explain(
        IReadOnlyList<SpamCapability> unearned,
        IReadOnlyDictionary<SpamCapability, SpamTrustTier> floors
    ) =>
        "Unearned capability: "
        + string.Join(", ", unearned.Select(c => $"{c} (needs {floors[c]} or above)"))
        + ".";

    /// <summary>
    /// True when most of the message's letters belong to a script other than Latin. A single foreign
    /// character in an otherwise Latin line is not "a message in another script"; that mixed case is the
    /// normalizer's own signal.
    /// </summary>
    private static bool IsWrittenInAnotherScript(string text)
    {
        int latin = 0;
        int other = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
                continue;
            if (IsLatinLetter(c))
                latin++;
            else
                other++;
        }

        return other > latin;
    }

    private static bool IsLatinLetter(char c) =>
        c
            is < FirstNonLatinLetter
                or (>= LatinExtendedAdditionalStart and <= LatinExtendedAdditionalEnd);
}
