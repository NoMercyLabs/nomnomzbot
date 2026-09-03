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
/// Every knob in the spam-defence stack (spam-defense.md §6.1).
///
/// <para>Nothing in this design is a magic number buried in code. Every threshold named anywhere in the
/// spec is a stored, editable setting with a documented default, a range, and a plain-language
/// explanation of what moving it costs — see <see cref="SpamSettingCatalogue"/>, which is enforced
/// against this record by test so a knob cannot ship without its explanation.</para>
///
/// <para>The same record is edited at two scopes: per channel, and as the platform-wide defaults every
/// new channel inherits. Same fields, same validation, same copy — only the scope differs.</para>
/// </summary>
public sealed record SpamDefenseSettings
{
    // ---- Master ---------------------------------------------------------------------------------

    /// <summary>Kill switch for the whole stack.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Detect and log, act on nothing. On for a channel's first 7 days, and a perfectly good permanent
    /// setting for a channel that only wants visibility.
    /// </summary>
    public bool DryRun { get; init; } = true;

    // ---- Trust ----------------------------------------------------------------------------------

    /// <summary>Thresholds for the earned ladder and the SD8 immunity gate.</summary>
    public TrustTierThresholds TrustThresholds { get; init; } = new();

    /// <summary>Watch hours in this channel that grant Semi-Trusted.</summary>
    public double SemiTrustedWatchHoursHere { get; init; } = 10;

    /// <summary>Watch hours across this instance's channels that grant Semi-Trusted.</summary>
    public double SemiTrustedWatchHoursInstance { get; init; } = 25;

    // ---- Content --------------------------------------------------------------------------------

    /// <summary>Similarity at which a message counts as a mutation of a known campaign.</summary>
    public double NearDuplicateSimilarity { get; init; } = 0.6;

    /// <summary>Skeletons shorter than this are never corpus-matched.</summary>
    public int MinimumSkeletonLength { get; init; } = 8;

    /// <summary>Restrict non-Latin script to viewers who have been around. Off by default (SD2).</summary>
    public bool NonLatinScriptGate { get; init; }

    // ---- Campaign -------------------------------------------------------------------------------

    /// <summary>Share of a cohort with no standing needed to call it a campaign.</summary>
    public double QualifyNoStandingShare { get; init; } = 0.80;

    /// <summary>Share below which a qualified cohort is exonerated and reversed.</summary>
    public double DequalifyNoStandingShare { get; init; } = 0.65;

    /// <summary>Fewest distinct accounts that can constitute a campaign.</summary>
    public int MinimumCohortSize { get; init; } = 5;

    /// <summary>Correlation window, extended by each new match.</summary>
    public int WindowSeconds { get; init; } = 600;

    /// <summary>Hard cap on the correlation window, from the first match.</summary>
    public int MaxWindowSeconds { get; init; } = 1800;

    /// <summary>The exoneration head start before a qualified campaign may act.</summary>
    public int ActionDelaySeconds { get; init; } = 8;

    /// <summary>Undo a campaign's actions when it de-qualifies. Off is allowed, and warned against.</summary>
    public bool AutoReverseOnDequalify { get; init; } = true;

    // ---- Bursts ---------------------------------------------------------------------------------

    /// <summary>Follow rate over the channel's own baseline that counts as a spike.</summary>
    public double FollowSpikeFactor { get; init; } = 5;

    /// <summary>Chatter-join rate over the channel's own baseline that counts as a burst.</summary>
    public double JoinBurstFactor { get; init; } = 4;

    // ---- Lockdown -------------------------------------------------------------------------------

    /// <summary>How long a lockdown holds before restoring the room by itself.</summary>
    public int LockdownMinutes { get; init; } = 15;

    /// <summary>Extend the window while the attack is still arriving.</summary>
    public bool LockdownAutoExtend { get; init; } = true;

    /// <summary>Ceiling on a lockdown however long the attack lasts.</summary>
    public int LockdownMaxMinutes { get; init; } = 60;

    // ---- Network --------------------------------------------------------------------------------

    /// <summary>Pull the shared signature set. Free, read-only, and on by default.</summary>
    public bool NetworkSubscribe { get; init; } = true;

    /// <summary>Send this channel's confirmed signatures back. Opt-in (SD3).</summary>
    public bool NetworkContribute { get; init; }

    /// <summary>Independent reporters needed before a quarantined signature may act.</summary>
    public int RequiredCorroborations { get; init; } = 3;
}

/// <summary>
/// How a setting is presented and bounded (spam-defense.md §6.1).
///
/// <para>Carries the resource KEYS for its copy, never the copy itself. The product ships in English
/// and Dutch, and the house rule is that the backend never holds user-facing prose — so the bounds and
/// the identity live here, where behaviour is, and the words live in the dashboard's string resources
/// where they can be translated.</para>
///
/// <para>The three keys are DERIVED from <see cref="Key"/> rather than stored, which removes the whole
/// class of bug where a descriptor points at a resource nobody wrote.</para>
/// </summary>
/// <param name="Key">The property name on <see cref="SpamDefenseSettings"/>.</param>
/// <param name="Group">The section it belongs to in the editor.</param>
/// <param name="Minimum">Lowest accepted value, or null for a toggle.</param>
/// <param name="Maximum">Highest accepted value, or null for a toggle.</param>
public sealed record SpamSettingDescriptor(
    string Key,
    string Group,
    double? Minimum = null,
    double? Maximum = null
)
{
    /// <summary>Resource key for the human-readable name.</summary>
    public string LabelKey => $"spam_setting_{SnakeKey}_label";

    /// <summary>Resource key for what the setting does.</summary>
    public string ExplanationKey => $"spam_setting_{SnakeKey}_explanation";

    /// <summary>
    /// Resource key for what moving it costs, in both directions. This is the string that makes the
    /// difference between a settings page and a machine somebody can actually operate: a number with a
    /// range but no stated consequence is a number nobody can tune honestly.
    /// </summary>
    public string CostKey => $"spam_setting_{SnakeKey}_cost";

    /// <summary>True for a toggle, which has no range to enforce.</summary>
    public bool IsToggle => Minimum is null && Maximum is null;

    private string SnakeKey => ToSnakeCase(Key);

    private static string ToSnakeCase(string name)
    {
        System.Text.StringBuilder builder = new(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[i]));
        }
        return builder.ToString();
    }
}

/// <summary>
/// Which knobs exist, what group they belong to, and what bounds they are held to — the structural
/// facts about the configuration surface.
///
/// <para>This lives in the Domain rather than the dashboard because it describes behaviour: the same
/// bounds that validate a save are the ones the editor renders, so there is no second list to keep in
/// step. A test walks <see cref="SpamDefenseSettings"/> by reflection and fails when a property has no
/// entry here, so a weight cannot ship unlisted; a matching test on the dashboard side fails when a
/// listed weight has no copy in English and Dutch, so it cannot ship unexplained either.</para>
///
/// <para>The five invariants are named too, because an operator should be able to see the guarantees
/// they get for free rather than have to ask.</para>
/// </summary>
public static class SpamSettingCatalogue
{
    /// <summary>Group keys, so the editor's section headings are translated the same way.</summary>
    public static class Groups
    {
        public const string Master = "master";
        public const string Trust = "trust";
        public const string Content = "content";
        public const string Campaign = "campaign";
        public const string Bursts = "bursts";
        public const string Lockdown = "lockdown";
        public const string Network = "network";
    }

    /// <summary>
    /// The decisions that have no switch. A toggle that turns off "never punish a regular" is a toggle
    /// somebody eventually flips at 3am during a raid, and then it is a person's account.
    /// </summary>
    public static IReadOnlyList<string> Invariants { get; } = ["SD0", "SD8", "SD9", "SD11", "SD12"];

    /// <summary>Resource key for what an invariant guarantees.</summary>
    public static string GuaranteeKey(string decision) =>
        $"spam_invariant_{decision.ToLowerInvariant()}_guarantee";

    /// <summary>Every knob, with its group and its bounds.</summary>
    public static IReadOnlyList<SpamSettingDescriptor> All { get; } =
    [
        new(nameof(SpamDefenseSettings.IsEnabled), Groups.Master),
        new(nameof(SpamDefenseSettings.DryRun), Groups.Master),
        new(nameof(SpamDefenseSettings.TrustThresholds), Groups.Trust),
        new(nameof(SpamDefenseSettings.SemiTrustedWatchHoursHere), Groups.Trust, 1, 200),
        new(nameof(SpamDefenseSettings.SemiTrustedWatchHoursInstance), Groups.Trust, 1, 500),
        new(nameof(SpamDefenseSettings.NearDuplicateSimilarity), Groups.Content, 0, 1),
        new(nameof(SpamDefenseSettings.MinimumSkeletonLength), Groups.Content, 2, 50),
        new(nameof(SpamDefenseSettings.NonLatinScriptGate), Groups.Content),
        new(nameof(SpamDefenseSettings.QualifyNoStandingShare), Groups.Campaign, 0.5, 1),
        new(nameof(SpamDefenseSettings.DequalifyNoStandingShare), Groups.Campaign, 0.3, 0.95),
        new(nameof(SpamDefenseSettings.MinimumCohortSize), Groups.Campaign, 2, 100),
        new(nameof(SpamDefenseSettings.WindowSeconds), Groups.Campaign, 30, 3600),
        new(nameof(SpamDefenseSettings.MaxWindowSeconds), Groups.Campaign, 60, 7200),
        new(nameof(SpamDefenseSettings.ActionDelaySeconds), Groups.Campaign, 0, 120),
        new(nameof(SpamDefenseSettings.AutoReverseOnDequalify), Groups.Campaign),
        new(nameof(SpamDefenseSettings.FollowSpikeFactor), Groups.Bursts, 1.5, 50),
        new(nameof(SpamDefenseSettings.JoinBurstFactor), Groups.Bursts, 1.5, 50),
        new(nameof(SpamDefenseSettings.LockdownMinutes), Groups.Lockdown, 1, 240),
        new(nameof(SpamDefenseSettings.LockdownAutoExtend), Groups.Lockdown),
        new(nameof(SpamDefenseSettings.LockdownMaxMinutes), Groups.Lockdown, 5, 480),
        new(nameof(SpamDefenseSettings.NetworkSubscribe), Groups.Network),
        new(nameof(SpamDefenseSettings.NetworkContribute), Groups.Network),
        new(nameof(SpamDefenseSettings.RequiredCorroborations), Groups.Network, 1, 20),
    ];

    /// <summary>The descriptor for a settings property, or null when there is none.</summary>
    public static SpamSettingDescriptor? For(string key) => All.FirstOrDefault(d => d.Key == key);
}
