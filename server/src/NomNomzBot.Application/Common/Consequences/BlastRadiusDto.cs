// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Consequences;

/// <summary>
/// One counted category of a destructive action's blast radius. <paramref name="CategoryKey"/> is an i18n
/// resource KEY, never a sentence — the language lives in the dashboard's string resources
/// (<c>strings.xml</c>), so the backend never ships English.
/// </summary>
/// <param name="CategoryKey">i18n resource key naming the kind of dependent that was counted.</param>
/// <param name="Count">The real number of rows counted right now. Never estimated.</param>
/// <param name="Sample">
/// Up to a handful of human-readable names of the counted rows, so the user recognises WHICH things break.
/// A shorter list than <paramref name="Count"/> means the sample was truncated, never that the count is soft.
/// </param>
public sealed record BlastRadiusCategoryDto(
    string CategoryKey,
    int Count,
    IReadOnlyList<string> Sample
);

/// <summary>
/// The real, counted set of things that reference a resource right now — surfaced in the delete confirmation
/// BEFORE the save (S-CONSEQ). <see cref="TotalReferences"/> is zero exactly when nothing references the
/// resource; the dashboard renders that as an explicit "nothing else references this" statement, never as an
/// empty area. A lookup that FAILED is a <c>Result</c> failure and never reaches this type — reporting zero
/// for a failed check would cause exactly the loss the preview exists to prevent.
/// </summary>
/// <param name="Categories">Only the categories with a non-zero count; an empty list means a genuine zero.</param>
/// <param name="IsMinimum">
/// True when the count is a verified FLOOR rather than an exhaustive total — set when some references can
/// only be resolved at run time (a pipeline field whose value is a template placeholder, or a user code
/// script that resolves the resource through the SDK). The dashboard must then say the number is a MINIMUM
/// rather than implying completeness.
/// </param>
public sealed record BlastRadiusDto(
    IReadOnlyList<BlastRadiusCategoryDto> Categories,
    bool IsMinimum
)
{
    public int TotalReferences => Categories.Sum(category => category.Count);
}

/// <summary>The i18n resource keys the blast-radius previews emit. One place, so no key is invented twice.</summary>
public static class BlastRadiusCategoryKeys
{
    public const string PipelineSteps = "blast_radius_category_pipeline_steps";
    public const string WidgetVersions = "blast_radius_category_widget_versions";
    public const string Redemptions = "blast_radius_category_redemptions";
    public const string RedemptionTimers = "blast_radius_category_redemption_timers";
    public const string GiveawayCodes = "blast_radius_category_giveaway_codes";
    public const string Giveaways = "blast_radius_category_giveaways";
}
