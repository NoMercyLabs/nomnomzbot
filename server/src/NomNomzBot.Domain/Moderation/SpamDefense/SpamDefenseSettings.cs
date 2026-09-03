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

/// <summary>How a setting is presented and bounded (spam-defense.md §6.1).</summary>
/// <param name="Key">The property name on <see cref="SpamDefenseSettings"/>.</param>
/// <param name="Group">The section it belongs to in the editor.</param>
/// <param name="Label">Plain-language name. Never the property name.</param>
/// <param name="Explanation">What the setting actually does, for someone who is not an engineer.</param>
/// <param name="CostOfMoving">
/// What it costs to move it, in both directions. This is the field that makes the difference between a
/// settings page and a machine somebody can actually operate: a number with a range but no consequence
/// is a number nobody can tune honestly.
/// </param>
/// <param name="Minimum">Lowest accepted value, or null for a toggle.</param>
/// <param name="Maximum">Highest accepted value, or null for a toggle.</param>
public sealed record SpamSettingDescriptor(
    string Key,
    string Group,
    string Label,
    string Explanation,
    string CostOfMoving,
    double? Minimum = null,
    double? Maximum = null
);

/// <summary>
/// The plain-language description of every knob, and of the five things that are deliberately NOT
/// knobs.
///
/// <para>This lives in the Domain rather than in the dashboard on purpose. If the explanations sat in
/// the UI they would drift from the behaviour the moment a threshold changed, and the operator would be
/// reading a description of a machine we no longer ship. Here, a test walks
/// <see cref="SpamDefenseSettings"/> by reflection and fails when a property has no entry — so adding a
/// weight without explaining it breaks the build.</para>
/// </summary>
public static class SpamSettingCatalogue
{
    /// <summary>
    /// The invariants that have no switch, and why. A toggle that turns off "never punish a regular" is
    /// a toggle somebody eventually flips at 3am during a raid, and then it is a person's account.
    /// </summary>
    public static IReadOnlyList<(string Decision, string Guarantee)> Invariants { get; } =
    [
        (
            "SD0",
            "Under uncertainty the room is tightened, never the person. Lockdown is always the "
                + "first response to a raid, never a mass ban."
        ),
        (
            "SD8",
            "An established regular of this channel is never actioned automatically, at any "
                + "confidence, by any layer."
        ),
        (
            "SD9",
            "Presence is never an offence. No account is actioned for being silent, being new, "
                + "or arriving during an attack. Every action needs that account's own evidence."
        ),
        (
            "SD11",
            "A viewer with standing — a mod or sub anywhere, or real watch time here — is never "
                + "automatically banned or timed out. The engine's ceiling for them is deleting "
                + "a message and flagging it."
        ),
        (
            "SD12",
            "We do not host the chat, so nothing here claims to stop a message before it is "
                + "published. What we can do is tighten the platform's own rules, and we say so "
                + "plainly rather than implying cover we do not have."
        ),
    ];

    /// <summary>Every knob, with the copy the editor shows.</summary>
    public static IReadOnlyList<SpamSettingDescriptor> All { get; } =
    [
        new(
            nameof(SpamDefenseSettings.IsEnabled),
            "Master",
            "Spam defence enabled",
            "Turns the whole system on or off. Off means no detection and no records at all.",
            "Off leaves you with only the platform's own tools."
        ),
        new(
            nameof(SpamDefenseSettings.DryRun),
            "Master",
            "Watch only, do not act",
            "Everything is detected and recorded with a full explanation, and nothing is acted "
                + "on. The dashboard shows what would have happened.",
            "Leaving it on means spam is never removed automatically. Turning it off is the "
                + "moment the system starts affecting real viewers — do it after you have read a "
                + "week of your own results and agree with them."
        ),
        new(
            nameof(SpamDefenseSettings.SemiTrustedWatchHoursHere),
            "Trust",
            "Watch hours here that earn protection",
            "Someone who has watched this many hours in your channel can no longer be banned or "
                + "timed out automatically, even if they have never typed a word.",
            "Lower protects quiet regulars sooner. Higher leaves them exposed for longer — this "
                + "is the setting that protects lurkers, who have no other way to prove they are "
                + "real.",
            1,
            200
        ),
        new(
            nameof(SpamDefenseSettings.SemiTrustedWatchHoursInstance),
            "Trust",
            "Watch hours across all channels that earn protection",
            "The same protection, earned across every channel on this server rather than just "
                + "yours.",
            "Lower extends trust between channels more readily; higher keeps each channel's "
                + "judgement to itself.",
            1,
            500
        ),
        new(
            nameof(SpamDefenseSettings.NearDuplicateSimilarity),
            "Content",
            "How similar counts as the same spam",
            "Spammers change a couple of characters and send again. This is how alike a message "
                + "must be to a known one to be treated as the same campaign.",
            "Lower catches more mutations but starts matching ordinary messages that happen to "
                + "share phrasing. Higher only catches near-exact repeats. 0 turns mutation "
                + "matching off entirely and leaves only exact matches.",
            0,
            1
        ),
        new(
            nameof(SpamDefenseSettings.MinimumSkeletonLength),
            "Content",
            "Shortest message that can be matched",
            "Messages shorter than this are never compared against known spam.",
            "Lower risks matching \"gg\" and \"lol\" against the shared list, which would delete "
                + "them everywhere at once. Higher lets short spam through.",
            2,
            50
        ),
        new(
            nameof(SpamDefenseSettings.NonLatinScriptGate),
            "Content",
            "Restrict other alphabets to established viewers",
            "Limits Japanese, Korean, Cyrillic, Arabic and other non-Latin messages to viewers "
                + "who have been around.",
            "Off by default and should stay off for almost everyone: real viewers write in these "
                + "alphabets every day. It exists for a channel under an active attack that uses "
                + "them, and it silences honest international viewers while it is on."
        ),
        new(
            nameof(SpamDefenseSettings.QualifyNoStandingShare),
            "Campaign",
            "Share of strangers that makes it a campaign",
            "When many accounts post the same thing at once, this is how much of that group must "
                + "be people with no standing in your channel before it is treated as coordinated "
                + "spam rather than a community joke.",
            "Lower catches campaigns sooner but starts treating copypasta as an attack. Higher "
                + "means a campaign needs to be almost entirely strangers before anything happens.",
            0.5,
            1
        ),
        new(
            nameof(SpamDefenseSettings.DequalifyNoStandingShare),
            "Campaign",
            "Share at which the group is exonerated",
            "If your regulars join in and the group falls below this, it is judged community "
                + "behaviour after all — everything already done is undone.",
            "Higher exonerates more readily and reverses more often. Lower makes reversal rarer. "
                + "It must stay below the qualifying share, which is what stops a group on the "
                + "line from flipping back and forth.",
            0.3,
            0.95
        ),
        new(
            nameof(SpamDefenseSettings.MinimumCohortSize),
            "Campaign",
            "Fewest accounts that can be a campaign",
            "Below this many distinct accounts, nothing is treated as coordinated however "
                + "identical the messages.",
            "Lower catches small coordinated groups but risks treating a few friends quoting each "
                + "other as an attack. Higher lets small campaigns through.",
            2,
            100
        ),
        new(
            nameof(SpamDefenseSettings.WindowSeconds),
            "Campaign",
            "How long a group is watched",
            "Messages this far apart still count as part of the same group. Each new match "
                + "extends it.",
            "Longer links slower campaigns together but keeps groups open longer. Shorter misses "
                + "campaigns that trickle.",
            30,
            3600
        ),
        new(
            nameof(SpamDefenseSettings.MaxWindowSeconds),
            "Campaign",
            "Longest a group can stay open",
            "However many messages keep arriving, a group closes after this.",
            "Longer lets one long attack stay a single incident; shorter splits it into several.",
            60,
            7200
        ),
        new(
            nameof(SpamDefenseSettings.ActionDelaySeconds),
            "Campaign",
            "Head start before acting",
            "After a group is judged a campaign, the system waits this long before doing "
                + "anything — long enough for one of your regulars to join in and prove it is a "
                + "joke.",
            "This is a real trade-off and it is yours to make. Longer means an exoneration almost "
                + "always beats the ban, at the cost of that many seconds of visible spam. "
                + "Shorter catches the spam faster and relies on undoing mistakes afterwards.",
            0,
            120
        ),
        new(
            nameof(SpamDefenseSettings.AutoReverseOnDequalify),
            "Campaign",
            "Undo automatically when a group is exonerated",
            "When regulars turn out to be part of a group, every timeout and ban it issued is "
                + "reversed on its own.",
            "Turning this off means somebody stays banned for the rest of your stream because "
                + "they laughed along, until a moderator notices. Strongly recommended on."
        ),
        new(
            nameof(SpamDefenseSettings.FollowSpikeFactor),
            "Bursts",
            "Follow rate that counts as unusual",
            "How many times your channel's normal follow rate counts as a spike worth looking at. "
                + "It is measured against your own history, so a small channel and a large one are "
                + "never compared to each other.",
            "Lower notices smaller spikes, including the ones caused by going viral. Note that a "
                + "spike alone never blocks anybody — it only decides when to look closely.",
            1.5,
            50
        ),
        new(
            nameof(SpamDefenseSettings.JoinBurstFactor),
            "Bursts",
            "Join rate that counts as unusual",
            "The same, for people arriving in chat rather than following.",
            "Lower notices smaller bursts. A raid from a friendly channel looks exactly like this, "
                + "which is why a burst never actions anyone by itself.",
            1.5,
            50
        ),
        new(
            nameof(SpamDefenseSettings.LockdownMinutes),
            "Lockdown",
            "How long the room stays tightened",
            "During a raid the platform's own rules are tightened — slow mode, followers-only and "
                + "so on — and put back exactly as they were after this long.",
            "Longer keeps the room calm but keeps honest new viewers out. Shorter reopens sooner "
                + "and may reopen into the same attack.",
            1,
            240
        ),
        new(
            nameof(SpamDefenseSettings.LockdownAutoExtend),
            "Lockdown",
            "Keep it tightened while the attack continues",
            "Extends the window as long as the attack is still arriving.",
            "Off means the room reopens on schedule even mid-raid."
        ),
        new(
            nameof(SpamDefenseSettings.LockdownMaxMinutes),
            "Lockdown",
            "Longest the room can stay tightened",
            "A ceiling, so a lockdown can never be forgotten about.",
            "Longer risks a room left restricted after everyone has gone home.",
            5,
            480
        ),
        new(
            nameof(SpamDefenseSettings.NetworkSubscribe),
            "Network",
            "Use the shared spam list",
            "Pulls known spam patterns and malicious links found by other servers. Read-only — "
                + "nothing about your channel leaves it.",
            "Off means you only ever catch what you have seen yourself."
        ),
        new(
            nameof(SpamDefenseSettings.NetworkContribute),
            "Network",
            "Share what you catch",
            "Sends the patterns your channel confirms back to the shared list. Never message "
                + "text, never viewer identities — a pattern and nothing else.",
            "Off by default. On helps everyone catch a campaign faster."
        ),
        new(
            nameof(SpamDefenseSettings.RequiredCorroborations),
            "Network",
            "Reports needed before a shared pattern acts",
            "A pattern from an unproven source only flags until this many independent servers "
                + "have seen it too.",
            "Lower acts on shared patterns sooner and trusts strangers more. Higher is the "
                + "protection against one bad contributor causing mass removals everywhere.",
            1,
            20
        ),
        new(
            nameof(SpamDefenseSettings.TrustThresholds),
            "Trust",
            "Trust ladder thresholds",
            "How long someone has been around, how much they have said, and on how many separate "
                + "days, before they count as a newcomer, a known face, a regular, or an "
                + "established member of your channel.",
            "Lower earns trust faster, which protects real viewers sooner and gives a patient "
                + "spammer a shorter road. Higher is stricter on both. Reaching established means "
                + "the system will never act against them automatically, which is why this "
                + "particular bar is worth setting deliberately."
        ),
    ];

    /// <summary>The descriptor for a settings property, or null when there is none.</summary>
    public static SpamSettingDescriptor? For(string key) => All.FirstOrDefault(d => d.Key == key);
}
