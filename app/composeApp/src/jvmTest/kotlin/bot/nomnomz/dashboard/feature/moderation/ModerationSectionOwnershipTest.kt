// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.fail

// The moderation surface was ONE page carrying 19 sections and four unrelated jobs: acting on a person
// mid-stream, working a queue, changing rules that apply forever, and reading history. S-UX-3 split it into
// four pages, one per job (frontend-ia.md §Moderation).
//
// A split like that decays silently. Someone adds a section and reaches for the nearest `item(...)`, and the
// page slowly refills with jobs it does not own — which is exactly how it got to 19 in the first place. So
// the assignment is pinned here: every list item must be owned by exactly one page, the four pages must stay
// disjoint, and moving a section between pages has to be a deliberate edit to this file.
class ModerationSectionOwnershipTest {

    private val screen: File =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/moderation/ui/ModerationScreen.kt")

    // Shown on every page: the title says which page you are on, and a failed write must surface wherever it
    // was triggered rather than only on the page that owns the list it changed.
    private val sharedByEveryPage: Set<String> = setOf("page-header", "unban-error")

    // The map decided in S-UX-1, by job. Changing a value here is a deliberate re-placement, not a refactor.
    private val expectedOwners: Map<String, Set<String>> =
        mapOf(
            // The live desk — act on a person, now.
            "Desk" to
                setOf(
                    "stats",
                    "shield-toggle",
                    "action-button",
                    "moderators-header",
                    "moderators-add",
                    "moderators-card",
                    "bans-header",
                    "bans-card",
                ),
            // Work a queue — decide the next item.
            "Queue" to
                setOf(
                    "unban-header",
                    "unban-card",
                    "reports-header",
                    "reports-card",
                    "automod-queue-header",
                    "automod-queue-card",
                    "spam-review-queue-header",
                    "spam-review-queue-card",
                    "spam-detections-header",
                    "spam-detections-card",
                    "spam-campaigns-header",
                    "spam-campaigns-card",
                    "spam-follow-blocks-header",
                    "spam-follow-blocks-card",
                ),
            // Change a rule — read carefully, visited rarely. Owner request 2026-09-12: the Rules page itself
            // used to be one flat scroll of 11 unrelated configuration surfaces. It is now split into job
            // groups (see rulesGroupOwners below) behind one tab strip ("rules-group-tabs" is that strip —
            // page chrome, not tied to any one group, so it stays outside the group map the same way
            // page-header/unban-error stay outside the page map).
            "Rules" to
                setOf(
                    "rules-group-tabs",
                    "terms-header",
                    "terms-unavailable",
                    "terms-add",
                    "terms-card",
                    "automod-header",
                    "automod-card",
                    "twitch-automod-card",
                    "trust-automation-header",
                    "automation-panel",
                    "trust-policy-card",
                    "spam-defense-card",
                    "heat-header",
                    "heat-card",
                    "escalation-header",
                    "escalation-card",
                    "shared-bans-header",
                    "shared-bans-card",
                    "rules-header",
                    "rules-card",
                    "chat-filters-header",
                    "chat-filters-card",
                    "shoutout-header",
                    "shoutout-card",
                ),
            // Find what happened.
            "History" to
                setOf(
                    "log-header",
                    "log-card",
                    "nuke-header",
                    "nuke-card",
                    "history-log-header",
                    "history-log-controls",
                    "history-log-card",
                    "history-log-pager",
                ),
        )

    // Ownership for the three pages that are still one flat list (Desk/Queue/History) plus the "rules-group-tabs"
    // chrome item, which the Rules page renders via the same plain helper because it must show regardless of
    // which job group is selected.
    private val ownedItem: Regex =
        Regex("""sectionItem\(section, ModerationSection\.(\w+), "([a-z0-9-]+)"\)""")

    // Everything else on the Rules page goes through rulesSectionItem, which is hardcoded to only ever emit on
    // ModerationSection.Rules (see the helper above `RulesScreen`'s BansList in ModerationScreen.kt) — so a
    // match here always belongs to the "Rules" page; the captured group is the job group within that page.
    private val ruleGroupItem: Regex =
        Regex("""rulesSectionItem\(section, RulesGroup\.(\w+), selectedRulesGroup, "([a-z0-9-]+)"\)""")

    private val unownedItem: Regex = Regex("""\bitem\(key = "([a-z0-9-]+)"\)""")

    // The job groups the Rules page's own tab strip switches between (S-UX-follow-on, owner request
    // 2026-09-12): filtering incoming content, automatic enforcement, trust extended to other channels, and
    // the one leftover surface (shoutout) that fits none of the three. Changing a value here is a deliberate
    // re-placement, not a refactor — same discipline as expectedOwners above.
    private val expectedRulesGroups: Map<String, Set<String>> =
        mapOf(
            "ContentFilters" to
                setOf(
                    "terms-header",
                    "terms-unavailable",
                    "terms-add",
                    "terms-card",
                    "automod-header",
                    "automod-card",
                    "twitch-automod-card",
                    "rules-header",
                    "rules-card",
                    "chat-filters-header",
                    "chat-filters-card",
                ),
            "AutoEnforcement" to
                setOf(
                    "trust-automation-header",
                    "automation-panel",
                    "trust-policy-card",
                    "spam-defense-card",
                    "heat-header",
                    "heat-card",
                    "escalation-header",
                    "escalation-card",
                ),
            "Network" to setOf("shared-bans-header", "shared-bans-card"),
            "General" to setOf("shoutout-header", "shoutout-card"),
        )

    @Test
    fun every_section_is_owned_by_exactly_one_page() {
        val source: String = screen.readText()

        val actual: Map<String, Set<String>> =
            (ownedItem.findAll(source).map { it.groupValues[1] to it.groupValues[2] } +
                    ruleGroupItem.findAll(source).map { "Rules" to it.groupValues[2] })
                .groupBy({ it.first }, { it.second })
                .mapValues { (_, keys) -> keys.toSet() }

        assertEquals(expectedOwners, actual, "a moderation section changed pages without updating this map")

        val allOwned: List<String> =
            ownedItem.findAll(source).map { it.groupValues[2] }.toList() +
                ruleGroupItem.findAll(source).map { it.groupValues[2] }.toList()
        val duplicated: List<String> = allOwned.groupBy { it }.filterValues { it.size > 1 }.keys.toList()
        if (duplicated.isNotEmpty()) {
            fail("these sections are emitted by more than one page: $duplicated")
        }
    }

    @Test
    fun every_rules_section_is_owned_by_exactly_one_job_group() {
        // Proves the Rules page's OWN internal split (the point of this slice) the same way the outer test
        // proves the four-page split: a deliberate map, checked against the source, that fails loudly — naming
        // the offending key — the moment a card is added without deciding which job it belongs to.
        val source: String = screen.readText()

        val actual: Map<String, Set<String>> =
            ruleGroupItem
                .findAll(source)
                .groupBy({ it.groupValues[1] }, { it.groupValues[2] })
                .mapValues { (_, keys) -> keys.toSet() }

        assertEquals(expectedRulesGroups, actual, "a Rules-page card changed job group without updating this map")

        val allGrouped: List<String> = ruleGroupItem.findAll(source).map { it.groupValues[2] }.toList()
        val duplicated: List<String> = allGrouped.groupBy { it }.filterValues { it.size > 1 }.keys.toList()
        if (duplicated.isNotEmpty()) {
            fail("these Rules cards are emitted by more than one job group: $duplicated")
        }
    }

    @Test
    fun no_section_escapes_the_ownership_map_by_using_a_plain_item() {
        // A plain `item(key = …)` renders on ALL FOUR pages. That is correct for the two shared chrome items
        // and wrong for anything else — it is how a section quietly lands back on every page.
        val stragglers: Set<String> =
            unownedItem.findAll(screen.readText()).map { it.groupValues[1] }.toSet() - sharedByEveryPage

        if (stragglers.isNotEmpty()) {
            fail(
                "these list items render on every moderation page: $stragglers — give each one an owning " +
                    "page with sectionItem(section, ModerationSection.X, \"key\"), or add it to " +
                    "sharedByEveryPage if it really is page chrome"
            )
        }
    }

    @Test
    fun each_page_carries_a_job_worth_opening_it_for() {
        // A page with nothing on it is a nav entry that wastes a click; this is the floor the split has to
        // clear to be an improvement rather than a rearrangement.
        expectedOwners.forEach { (page, keys) ->
            if (keys.isEmpty()) fail("$page has no sections — it should not be a page")
        }
    }

    @Test
    fun the_four_pages_together_still_carry_everything_the_one_page_did() {
        // The split must not LOSE capability — the owner asked for less complexity, not fewer features.
        // 49 items were counted on the page before the split: 47 owned + the 2 shared chrome items. History
        // grew by 4 (owner punch list 2026-09-08 §12 — the browsable filtered log: header, filter controls,
        // list card, pager) when the empty-hiding mod-log/nuke-batch bug fix reused their EXISTING keys
        // rather than adding new ones. The Rules page then grew by 3 (owner request 2026-09-12, this slice):
        // "rules-group-tabs" (the new job-group switch itself) plus "heat-header"/"heat-card" (heat auto-timeout
        // split out of the AutoMod card into its own Enforcement-group card — no control lost, just relocated).
        val owned: Int = expectedOwners.values.sumOf { it.size }
        assertEquals(54, owned, "a moderation section was dropped or added without a decision")
    }
}
