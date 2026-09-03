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
/// The result of running a chat message through the L0 normalizer (spam-defense.md §L0). Carries both
/// forms plus the audit of what was stripped, because every enforcement decision above this layer has to
/// be explainable (SD7) — a moderator sees the original, the skeleton it matched on, and exactly which
/// evasion tricks were present.
/// </summary>
/// <param name="Original">
/// The message exactly as sent. The normalizer NEVER mutates what chat displays; it only decides.
/// </param>
/// <param name="Skeleton">
/// The match skeleton: lowercase, de-leeted, homoglyph-folded, repeat-collapsed, stripped of everything
/// that is not a letter or digit. This is what corpus and near-duplicate matching compare.
/// </param>
/// <param name="StrippedCosmeticAbuse">
/// True when the message carried combining marks or invisible/format characters. Per SD2 there is no
/// legitimate reason to put a zero-width joiner in a chat line, so this is a standalone high-confidence
/// signal — still bounded by the standing ceilings (SD8/SD11), which cap what may be done about it.
/// </param>
/// <param name="MixedScriptTokens">
/// Tokens that mixed two scripts BEFORE folding (`ѕtream` — Cyrillic ѕ inside a Latin word). Recorded
/// here rather than inferred later, because after folding the evidence is gone. Near-zero false-positive
/// rate, and explicitly NOT the same thing as a message written wholly in another script, which is
/// ordinary chat for a lot of channels.
/// </param>
public sealed record NormalizedMessage(
    string Original,
    string Skeleton,
    bool StrippedCosmeticAbuse,
    IReadOnlyList<string> MixedScriptTokens
)
{
    /// <summary>True when nothing survived normalization (punctuation or emoji only).</summary>
    public bool IsEmpty => Skeleton.Length == 0;
}
