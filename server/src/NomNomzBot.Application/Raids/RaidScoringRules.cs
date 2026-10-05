// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Raids;

/// <summary>
/// One channel's raid-target preferences. The scorer owns no rule of its own: every point it hands
/// out comes from this object. A rule with weight 0 adds nothing and shows no reason.
/// </summary>
/// <param name="PreferredCategories">Categories this channel likes, matched by name ignoring case, first match wins.</param>
/// <param name="OtherCategoryWeight">Points for a category that is not in <paramref name="PreferredCategories"/>.</param>
/// <param name="OtherCategoryLabel">Reason text for that case; the category name is appended in brackets.</param>
/// <param name="Keywords">Tag and title signals, checked in order, first matching rule wins.</param>
/// <param name="ViewerBands">Viewer-count bands, checked in order, first band that holds the count wins.</param>
/// <param name="Recency">How long ago this channel last raided the candidate.</param>
/// <param name="Balance">Who owes whom a raid.</param>
/// <param name="FollowedWeight">Points when the broadcaster follows the candidate.</param>
public sealed record RaidScoringRules(
    IReadOnlyList<RaidCategoryRule> PreferredCategories,
    int OtherCategoryWeight,
    string OtherCategoryLabel,
    IReadOnlyList<RaidKeywordRule> Keywords,
    IReadOnlyList<RaidViewerBand> ViewerBands,
    RaidRecencyRules Recency,
    RaidBalanceWeights Balance,
    int FollowedWeight
)
{
    /// <summary>
    /// No preference at all: every candidate scores 0, so a new channel ranks by the stable input
    /// order until its owner sets rules.
    /// </summary>
    public static RaidScoringRules Neutral { get; } =
        new(
            PreferredCategories: [],
            OtherCategoryWeight: 0,
            OtherCategoryLabel: "",
            Keywords: [],
            ViewerBands: [],
            Recency: new RaidRecencyRules(0, [], 0, ""),
            Balance: new RaidBalanceWeights(0, 0, 0),
            FollowedWeight: 0
        );
}

/// <summary>A liked category: its name, its points and the reason text.</summary>
public sealed record RaidCategoryRule(string Name, int Weight, string Label);

/// <summary>
/// A signal made of plain words or phrases. A word matches a whole tag, or whole words in the
/// title, ignoring case and punctuation. No regular expression is involved.
/// </summary>
public sealed record RaidKeywordRule(
    string Label,
    int Weight,
    IReadOnlyList<string> Words,
    bool MatchTags,
    bool MatchTitle
);

/// <summary>A viewer-count band, both ends included.</summary>
public sealed record RaidViewerBand(int Min, int Max, int Weight, string Label);

/// <summary>A raid that happened less than <paramref name="WithinDays"/> days ago.</summary>
public sealed record RaidRecencyStep(int WithinDays, int Weight, string Note);

/// <summary>Points by time since the last outgoing raid: never, within each step, or older than all steps.</summary>
public sealed record RaidRecencyRules(
    int NeverRaidedWeight,
    IReadOnlyList<RaidRecencyStep> Steps,
    int OlderWeight,
    string OlderNote
);

/// <summary>
/// Points when they raided us more than we raided them: the base for one owed raid, the extra for
/// each further one, and the points when raids are level.
/// </summary>
public sealed record RaidBalanceWeights(
    int OwedBaseWeight,
    int OwedExtraWeight,
    int ReciprocalWeight
);
