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

import bot.nomnomz.dashboard.core.network.AutomodBannedPhrases
import bot.nomnomz.dashboard.core.network.AutomodCapsFilter
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.AutomodEmoteSpam
import bot.nomnomz.dashboard.core.network.AutomodLinkFilter
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.EscalationLadderStep
import bot.nomnomz.dashboard.core.network.EscalationPolicy
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// The "what happens automatically" panel is only worth having if it tracks the real configuration. These tests
// change one config value at a time and assert the emitted line changes with it — a panel that kept claiming the
// same thing would fail here.
class AutomationSummaryTest {

    private fun derive(
        automod: AutomodConfig = AutomodConfig(),
        twitchAutoMod: TwitchAutoModSettings? = TwitchAutoModSettings(),
        chatFilters: List<ChatFilter> = emptyList(),
        rules: List<ModerationRule> = emptyList(),
        escalationPolicy: EscalationPolicy? = null,
    ): List<AutomationLine> =
        deriveAutomationLines(automod, twitchAutoMod, chatFilters, rules, escalationPolicy)

    @Test
    fun heat_only_flags_while_the_auto_timeout_switch_is_off() {
        val lines: List<AutomationLine> =
            derive(AutomodConfig(heatTimeoutThreshold = 80, autoTimeoutOnHeat = false))

        assertEquals(
            AutomationLine.HeatAutoTimeoutOff(threshold = 80),
            lines.filterIsInstance<AutomationLine.HeatAutoTimeoutOff>().single(),
        )
        assertTrue(lines.none { it is AutomationLine.HeatAutoTimeoutOn })
    }

    @Test
    fun heat_line_flips_to_a_real_timeout_when_the_switch_is_turned_on() {
        val off: AutomodConfig = AutomodConfig(heatTimeoutThreshold = 80, heatTimeoutSeconds = 600)
        val on: AutomodConfig = off.copy(autoTimeoutOnHeat = true)

        val beforeLine: AutomationLine = derive(off).first { it is AutomationLine.HeatAutoTimeoutOff }
        val afterLine: AutomationLine = derive(on).first { it is AutomationLine.HeatAutoTimeoutOn }

        assertEquals(AutomationLine.HeatAutoTimeoutOff(threshold = 80), beforeLine)
        assertEquals(AutomationLine.HeatAutoTimeoutOn(threshold = 80, seconds = 600), afterLine)
    }

    @Test
    fun twitch_levels_that_could_not_be_read_are_unknown_never_off() {
        assertTrue(
            derive(twitchAutoMod = null).any { it is AutomationLine.TwitchAutoModUnavailable },
            "an unread Twitch AutoMod state must not be reported as off",
        )
        assertTrue(derive(twitchAutoMod = TwitchAutoModSettings()).any { it is AutomationLine.TwitchAutoModOff })
    }

    @Test
    fun twitch_reports_the_overall_dial_or_the_strictest_category() {
        assertEquals(
            AutomationLine.TwitchAutoModOverall(level = 3),
            derive(twitchAutoMod = TwitchAutoModSettings(overallLevel = 3)).first(),
        )
        assertEquals(
            AutomationLine.TwitchAutoModPerCategory(activeCategories = 2, strictest = 4),
            derive(twitchAutoMod = TwitchAutoModSettings(aggression = 4, swearing = 2)).first(),
        )
    }

    @Test
    fun each_enabled_local_filter_gets_its_own_line_and_an_all_off_config_says_so() {
        val allOff: List<AutomationLine> = derive()
        assertTrue(allOff.any { it is AutomationLine.LocalFiltersOff })

        val lines: List<AutomationLine> =
            derive(
                AutomodConfig(
                    linkFilter = AutomodLinkFilter(enabled = true, whitelist = listOf("clips.twitch.tv")),
                    capsFilter = AutomodCapsFilter(enabled = true, threshold = 70),
                    bannedPhrases = AutomodBannedPhrases(enabled = true, phrases = listOf("a", "b", "c")),
                    emoteSpam = AutomodEmoteSpam(enabled = true, maxEmotes = 8),
                )
            )

        assertEquals(
            listOf(
                AutomationLine.LocalFilterOn(AutomodFilter.Link, 1),
                AutomationLine.LocalFilterOn(AutomodFilter.Caps, 70),
                AutomationLine.LocalFilterOn(AutomodFilter.Phrases, 3),
                AutomationLine.LocalFilterOn(AutomodFilter.Emotes, 8),
            ),
            lines.filterIsInstance<AutomationLine.LocalFilterOn>(),
        )
        assertTrue(lines.none { it is AutomationLine.LocalFiltersOff })
    }

    @Test
    fun a_disabled_escalation_ladder_contributes_no_steps() {
        val disabled: EscalationPolicy =
            EscalationPolicy(
                isEnabled = false,
                ladder = listOf(EscalationLadderStep(atOffense = 3, action = "ban")),
            )

        val lines: List<AutomationLine> = derive(escalationPolicy = disabled)

        assertTrue(lines.any { it is AutomationLine.EscalationOff })
        assertTrue(lines.none { it is AutomationLine.EscalationStep })
        // A ban rung on a switched-off ladder bans nobody — it must not appear as an auto-ban path.
        assertTrue(lines.any { it is AutomationLine.AutoBanNone })
    }

    @Test
    fun an_enabled_ladder_lists_its_steps_in_order_and_names_the_ban_rung() {
        val enabled: EscalationPolicy =
            EscalationPolicy(
                isEnabled = true,
                ladder =
                    listOf(
                        EscalationLadderStep(atOffense = 3, action = "ban"),
                        EscalationLadderStep(atOffense = 1, action = "timeout", timeoutSeconds = 60),
                    ),
                offenseWindowHours = 168,
            )

        val lines: List<AutomationLine> = derive(escalationPolicy = enabled)

        assertEquals(
            listOf(
                AutomationLine.EscalationStep(atOffense = 1, action = "timeout", timeoutSeconds = 60),
                AutomationLine.EscalationStep(atOffense = 3, action = "ban", timeoutSeconds = null),
            ),
            lines.filterIsInstance<AutomationLine.EscalationStep>(),
        )
        assertEquals(
            AutomationLine.AutoBanFrom(listOf(AutoBanSource.EscalationLadder(atOffense = 3))),
            lines.filterIsInstance<AutomationLine.AutoBanFrom>().single(),
        )
    }

    @Test
    fun the_auto_ban_line_names_every_banning_filter_and_rule_but_only_the_enabled_ones() {
        val filters: List<ChatFilter> =
            listOf(
                ChatFilter(id = "f1", name = "Slur list", action = "ban", isEnabled = true),
                ChatFilter(id = "f2", name = "Off slur list", action = "ban", isEnabled = false),
                ChatFilter(id = "f3", name = "Link spam", action = "timeout", isEnabled = true),
            )
        val rules: List<ModerationRule> =
            listOf(
                ModerationRule(id = 1, name = "Bot raid", action = "ban", isEnabled = true),
                ModerationRule(id = 2, name = "Caps", action = "delete", isEnabled = true),
            )

        val lines: List<AutomationLine> = derive(chatFilters = filters, rules = rules)

        assertEquals(
            AutomationLine.AutoBanFrom(
                listOf(AutoBanSource.CustomFilter("Slur list"), AutoBanSource.Rule("Bot raid"))
            ),
            lines.filterIsInstance<AutomationLine.AutoBanFrom>().single(),
        )
        assertEquals(
            AutomationLine.CustomFiltersOn(count = 2, banning = 1),
            lines.filterIsInstance<AutomationLine.CustomFiltersOn>().single(),
        )
        assertEquals(
            AutomationLine.RulesOn(count = 2, banning = 1),
            lines.filterIsInstance<AutomationLine.RulesOn>().single(),
        )
    }

    @Test
    fun a_channel_with_nothing_switched_on_states_every_control_is_off() {
        val lines: List<AutomationLine> = derive()

        assertTrue(lines.any { it is AutomationLine.TwitchAutoModOff })
        assertTrue(lines.any { it is AutomationLine.LocalFiltersOff })
        assertTrue(lines.any { it is AutomationLine.CustomFiltersOff })
        assertTrue(lines.any { it is AutomationLine.RulesOff })
        assertTrue(lines.any { it is AutomationLine.EscalationOff })
        assertTrue(lines.any { it is AutomationLine.HeatAutoTimeoutOff })
        assertTrue(lines.any { it is AutomationLine.AutoBanNone })
    }
}
