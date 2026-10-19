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
/// The one place each raid scoring default is written. A channel with no saved rules, or a saved blob that
/// leaves a key out, resolves to these values. The record parameters, <see cref="RaidScoringRules.Neutral"/>,
/// the store and the API all read them. A default carries no streamer's taste: every weight is 0 and every
/// list is empty, so a new channel ranks by the stable input order until its owner sets rules.
/// </summary>
public static class RaidScoringDefaults
{
    public static readonly IReadOnlyList<RaidCategoryRule> PreferredCategories = [];
    public const int OtherCategoryWeight = 0;
    public const string OtherCategoryLabel = "";
    public static readonly IReadOnlyList<RaidKeywordRule> Keywords = [];
    public static readonly IReadOnlyList<RaidViewerBand> ViewerBands = [];

    public const int NeverRaidedWeight = 0;
    public static readonly IReadOnlyList<RaidRecencyStep> RecencySteps = [];
    public const int OlderWeight = 0;
    public const string OlderNote = "";

    public const int OwedBaseWeight = 0;
    public const int OwedExtraWeight = 0;
    public const int ReciprocalWeight = 0;

    public const int FollowedWeight = 0;

    /// <summary>The recency rules of a channel that set none.</summary>
    public static RaidRecencyRules Recency =>
        new(NeverRaidedWeight, RecencySteps, OlderWeight, OlderNote);

    /// <summary>The balance weights of a channel that set none.</summary>
    public static RaidBalanceWeights Balance =>
        new(OwedBaseWeight, OwedExtraWeight, ReciprocalWeight);

    /// <summary>
    /// Fills every key a stored blob or a request body left out. A scalar left out already carries its default
    /// from the record parameter; this fills the lists, the nested objects and the texts, which the
    /// deserializer leaves null.
    /// </summary>
    public static RaidScoringRules Resolve(RaidScoringRules? raw)
    {
        if (raw is null)
            return RaidScoringRules.Neutral;

        RaidRecencyRules? recency = raw.Recency;
        RaidBalanceWeights? balance = raw.Balance;
        IReadOnlyList<RaidRecencyStep>? steps = recency?.Steps;
        string? olderNote = recency?.OlderNote;
        string? otherLabel = raw.OtherCategoryLabel;

        return raw with
        {
            PreferredCategories = raw.PreferredCategories ?? PreferredCategories,
            OtherCategoryLabel = otherLabel ?? OtherCategoryLabel,
            Keywords = raw.Keywords ?? Keywords,
            ViewerBands = raw.ViewerBands ?? ViewerBands,
            Recency = recency is null
                ? Recency
                : recency with
                {
                    Steps = steps ?? RecencySteps,
                    OlderNote = olderNote ?? OlderNote,
                },
            Balance = balance ?? Balance,
        };
    }
}
