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
    public const string GiveawayEntries = "blast_radius_category_giveaway_entries";
    public const string GiveawayWinners = "blast_radius_category_giveaway_winners";
    public const string CodeScriptVersions = "blast_radius_category_code_script_versions";
    public const string CatalogPurchases = "blast_radius_category_catalog_purchases";
    public const string LeaderboardSnapshots = "blast_radius_category_leaderboard_snapshots";
    public const string SupporterConnections = "blast_radius_category_supporter_connections";
    public const string DiscordNotificationRules =
        "blast_radius_category_discord_notification_rules";
    public const string DiscordRoleButtons = "blast_radius_category_discord_role_buttons";
    public const string Pipelines = "blast_radius_category_pipelines";
    public const string Commands = "blast_radius_category_commands";
    public const string Widgets = "blast_radius_category_widgets";
    public const string SoundClips = "blast_radius_category_sound_clips";
    public const string Assets = "blast_radius_category_assets";
    public const string CustomDataSources = "blast_radius_category_custom_data_sources";
    public const string EventResponses = "blast_radius_category_event_responses";
    public const string Rewards = "blast_radius_category_rewards";
    public const string Timers = "blast_radius_category_timers";
    public const string ChatTriggers = "blast_radius_category_chat_triggers";
    public const string PickLists = "blast_radius_category_pick_lists";
    public const string CodeScripts = "blast_radius_category_code_scripts";
}
