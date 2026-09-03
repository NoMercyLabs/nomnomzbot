// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.state

import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.EscalationPolicy
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings

// "What happens automatically" — DERIVED, never asserted.
//
// The panel on the Moderation screen may state only what the channel's live configuration actually does, so this
// file computes the whole list from the very same config objects enforcement reads (the AutoMod config, Twitch's
// own AutoMod levels, the custom chat filters, the moderation rules, the escalation ladder). Nothing here is a
// hardcoded claim: a control that is off produces an explicit "off" line rather than silence, and the auto-ban
// line names the exact sources that can end in an automatic ban — or says nothing can.
//
// It yields VALUES, not sentences: the screen maps each line to its localized string (translations never live in
// code). That also makes the truthfulness testable without Compose — flip `autoTimeoutOnHeat` and the emitted
// line changes.

/** The Twitch-AutoMod wire value that means "this category is not filtered". */
private const val TWITCH_AUTOMOD_OFF: Int = 0

/** The action word the backend uses for a ban on a chat filter, a moderation rule and an escalation step. */
private const val BAN_ACTION: String = "ban"

/** One computed statement about what the bot does without asking a human. */
sealed interface AutomationLine {
    /** Twitch's AutoMod levels could not be read here (missing scope / no broadcaster token) — state is unknown. */
    data object TwitchAutoModUnavailable : AutomationLine

    /** Twitch AutoMod runs on one overall dial at [level] (1–4). */
    data class TwitchAutoModOverall(val level: Int) : AutomationLine

    /** Twitch AutoMod runs per category: [activeCategories] categories filter, the strictest at [strictest]. */
    data class TwitchAutoModPerCategory(val activeCategories: Int, val strictest: Int) : AutomationLine

    /** Twitch AutoMod filters nothing — every level is 0. */
    data object TwitchAutoModOff : AutomationLine

    /** A local AutoMod filter is on; [detail] is its threshold (caps %, max emotes) or its list size. */
    data class LocalFilterOn(val filter: AutomodFilter, val detail: Int?) : AutomationLine

    /** None of the four local AutoMod filters is on. */
    data object LocalFiltersOff : AutomationLine

    /** [count] custom chat filters are enabled, [banning] of which ban on a hit. */
    data class CustomFiltersOn(val count: Int, val banning: Int) : AutomationLine

    /** No custom chat filter is enabled. */
    data object CustomFiltersOff : AutomationLine

    /** [count] moderation rules are enabled, [banning] of which ban on a hit. */
    data class RulesOn(val count: Int, val banning: Int) : AutomationLine

    /** No moderation rule is enabled. */
    data object RulesOff : AutomationLine

    /** Crossing [threshold] heat times the viewer out for [seconds] — the bot acts on its own. */
    data class HeatAutoTimeoutOn(val threshold: Int, val seconds: Int) : AutomationLine

    /** Crossing [threshold] heat only FLAGS the viewer for a human — the bot issues no timeout. */
    data class HeatAutoTimeoutOff(val threshold: Int) : AutomationLine

    /** One rung of the live escalation ladder: at [atOffense] offenses the bot applies [action]. */
    data class EscalationStep(
        val atOffense: Int,
        val action: String,
        val timeoutSeconds: Int?,
    ) : AutomationLine

    /** The escalation ladder is off — repeat offenses escalate nothing automatically. */
    data object EscalationOff : AutomationLine

    /** These — and only these — can end in an automatic ban. */
    data class AutoBanFrom(val sources: List<AutoBanSource>) : AutomationLine

    /** Nothing on this channel can auto-ban. */
    data object AutoBanNone : AutomationLine
}

/** A configured path that ends in an automatic ban. */
sealed interface AutoBanSource {
    /** The escalation ladder bans at [atOffense] offenses inside the policy window. */
    data class EscalationLadder(val atOffense: Int) : AutoBanSource

    /** The enabled custom chat filter [name] bans on a hit. */
    data class CustomFilter(val name: String) : AutoBanSource

    /** The enabled moderation rule [name] bans on a hit. */
    data class Rule(val name: String) : AutoBanSource
}

/**
 * Compute what this channel does automatically, from the live configuration only.
 *
 * [twitchAutoMod] is null when the Twitch read failed — that yields "unknown", never "off" (an unread setting must
 * never be reported as an inactive one). Every other input is the object the screen already loaded.
 */
fun deriveAutomationLines(
    automod: AutomodConfig,
    twitchAutoMod: TwitchAutoModSettings?,
    chatFilters: List<ChatFilter>,
    rules: List<ModerationRule>,
    escalationPolicy: EscalationPolicy?,
): List<AutomationLine> {
    val lines: MutableList<AutomationLine> = mutableListOf()

    lines.add(twitchAutoModLine(twitchAutoMod))
    lines.addAll(localFilterLines(automod))

    val enabledFilters: List<ChatFilter> = chatFilters.filter { it.isEnabled }
    val banningFilters: List<ChatFilter> =
        enabledFilters.filter { it.action.equals(BAN_ACTION, ignoreCase = true) }
    lines.add(
        if (enabledFilters.isEmpty()) AutomationLine.CustomFiltersOff
        else AutomationLine.CustomFiltersOn(enabledFilters.size, banningFilters.size)
    )

    val enabledRules: List<ModerationRule> = rules.filter { it.isEnabled }
    val banningRules: List<ModerationRule> =
        enabledRules.filter { it.action.equals(BAN_ACTION, ignoreCase = true) }
    lines.add(
        if (enabledRules.isEmpty()) AutomationLine.RulesOff
        else AutomationLine.RulesOn(enabledRules.size, banningRules.size)
    )

    // The heat line is the one this slice turned honest: the auto-timeout is opt-in, so it may only claim a
    // timeout when the channel actually switched it on. Off, a crossing just flags the viewer for a human.
    lines.add(
        if (automod.autoTimeoutOnHeat) {
            AutomationLine.HeatAutoTimeoutOn(automod.heatTimeoutThreshold, automod.heatTimeoutSeconds)
        } else {
            AutomationLine.HeatAutoTimeoutOff(automod.heatTimeoutThreshold)
        }
    )

    val ladder: EscalationPolicy? = escalationPolicy?.takeIf { it.isEnabled }
    if (ladder == null) {
        lines.add(AutomationLine.EscalationOff)
    } else {
        ladder.ladder
            .sortedBy { it.atOffense }
            .forEach { step ->
                lines.add(AutomationLine.EscalationStep(step.atOffense, step.action, step.timeoutSeconds))
            }
    }

    val autoBanSources: List<AutoBanSource> =
        buildList {
            ladder
                ?.ladder
                ?.filter { it.action.equals(BAN_ACTION, ignoreCase = true) }
                ?.minByOrNull { it.atOffense }
                ?.let { step -> add(AutoBanSource.EscalationLadder(step.atOffense)) }
            banningFilters.forEach { filter -> add(AutoBanSource.CustomFilter(filter.name)) }
            banningRules.forEach { rule -> add(AutoBanSource.Rule(rule.name)) }
        }
    lines.add(
        if (autoBanSources.isEmpty()) AutomationLine.AutoBanNone
        else AutomationLine.AutoBanFrom(autoBanSources)
    )

    return lines
}

/** Twitch's own AutoMod: unread, one overall dial, per-category, or filtering nothing. */
private fun twitchAutoModLine(settings: TwitchAutoModSettings?): AutomationLine {
    if (settings == null) return AutomationLine.TwitchAutoModUnavailable
    val overall: Int? = settings.overallLevel
    if (overall != null) {
        return if (overall <= TWITCH_AUTOMOD_OFF) AutomationLine.TwitchAutoModOff
        else AutomationLine.TwitchAutoModOverall(overall)
    }
    val active: List<Int> = twitchCategoryLevels(settings).filter { it > TWITCH_AUTOMOD_OFF }
    return if (active.isEmpty()) AutomationLine.TwitchAutoModOff
    else AutomationLine.TwitchAutoModPerCategory(active.size, active.max())
}

/** The eight per-category levels, in the order the form renders them. */
fun twitchCategoryLevels(settings: TwitchAutoModSettings): List<Int> =
    listOf(
        settings.aggression,
        settings.bullying,
        settings.disability,
        settings.misogyny,
        settings.raceEthnicityOrReligion,
        settings.sexBasedTerms,
        settings.sexualitySexOrGender,
        settings.swearing,
    )

/** One line per enabled local filter, or a single explicit "all off" line. */
private fun localFilterLines(automod: AutomodConfig): List<AutomationLine> {
    val lines: MutableList<AutomationLine> = mutableListOf()
    if (automod.linkFilter.enabled) {
        lines.add(AutomationLine.LocalFilterOn(AutomodFilter.Link, automod.linkFilter.whitelist.size))
    }
    if (automod.capsFilter.enabled) {
        lines.add(AutomationLine.LocalFilterOn(AutomodFilter.Caps, automod.capsFilter.threshold))
    }
    if (automod.bannedPhrases.enabled) {
        lines.add(AutomationLine.LocalFilterOn(AutomodFilter.Phrases, automod.bannedPhrases.phrases.size))
    }
    if (automod.emoteSpam.enabled) {
        lines.add(AutomationLine.LocalFilterOn(AutomodFilter.Emotes, automod.emoteSpam.maxEmotes))
    }
    return lines.ifEmpty { listOf(AutomationLine.LocalFiltersOff) }
}
