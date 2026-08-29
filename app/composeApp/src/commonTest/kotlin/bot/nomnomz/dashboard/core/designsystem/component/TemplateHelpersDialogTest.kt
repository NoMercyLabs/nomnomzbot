// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import kotlin.test.Test
import kotlin.test.assertEquals

// Proves the two pure functions behind the "All helpers" dialog (S043): search must actually narrow the
// list to matching keys (never all-or-nothing), and grouping must put every helper under the namespace a
// streamer would expect (the segment before the first `.`, or the bare key itself when there is none) —
// not merely "renders without throwing".
class TemplateHelpersDialogTest {
    private val helpers: List<TemplateHelperDto> =
        listOf(
            TemplateHelperDto(key = "user.name", descriptionKey = "template.helper.user_name"),
            TemplateHelperDto(key = "user.id", descriptionKey = "template.helper.user_id"),
            TemplateHelperDto(key = "target.name", descriptionKey = "template.helper.target_name"),
            TemplateHelperDto(key = "botname", descriptionKey = "template.helper.botname"),
            TemplateHelperDto(key = "random.number.<n>", descriptionKey = "template.helper.random_number"),
        )

    @Test
    fun `blank query returns every helper unchanged`() {
        assertEquals(helpers, filterTemplateHelpers(helpers, ""))
        assertEquals(helpers, filterTemplateHelpers(helpers, "   "))
    }

    @Test
    fun `query narrows to only matching keys, case-insensitively`() {
        val result: List<TemplateHelperDto> = filterTemplateHelpers(helpers, "USER")

        assertEquals(listOf("user.name", "user.id"), result.map { it.key })
    }

    @Test
    fun `query matching nothing returns an empty list, not the full catalogue`() {
        val result: List<TemplateHelperDto> = filterTemplateHelpers(helpers, "nonexistent-helper-zzz")

        assertEquals(emptyList(), result)
    }

    @Test
    fun `query matches a substring anywhere in the key, not only a prefix`() {
        val result: List<TemplateHelperDto> = filterTemplateHelpers(helpers, "name")

        // "name" is a substring of user.name, target.name AND botname — proving the match is NOT
        // anchored to a prefix.
        assertEquals(listOf("user.name", "target.name", "botname"), result.map { it.key })
    }

    @Test
    fun `groups by the namespace before the first dot`() {
        val grouped: List<Pair<String, List<TemplateHelperDto>>> = groupTemplateHelpers(helpers)

        assertEquals(listOf("botname", "random", "target", "user"), grouped.map { it.first })
        assertEquals(
            listOf("user.name", "user.id"),
            grouped.first { it.first == "user" }.second.map { it.key },
        )
    }

    @Test
    fun `a key with no dot is grouped under itself`() {
        val grouped: List<Pair<String, List<TemplateHelperDto>>> = groupTemplateHelpers(helpers)

        val botnameGroup: List<TemplateHelperDto> = grouped.first { it.first == "botname" }.second
        assertEquals(listOf("botname"), botnameGroup.map { it.key })
    }

    @Test
    fun `search and grouping compose - a narrowed result groups correctly`() {
        val narrowed: List<TemplateHelperDto> = filterTemplateHelpers(helpers, "user")
        val grouped: List<Pair<String, List<TemplateHelperDto>>> = groupTemplateHelpers(narrowed)

        assertEquals(listOf("user" to listOf(helpers[0], helpers[1])), grouped)
    }
}
