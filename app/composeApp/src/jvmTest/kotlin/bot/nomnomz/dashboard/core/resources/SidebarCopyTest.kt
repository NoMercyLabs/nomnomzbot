// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.resources

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.fail

// S-IA-NAV copy guard: the sidebar group and page names are the ones the settled layout names, and no screen text
// says "Super Mod" (the role is Lead Moderator; "supermod" stays only as the server's wire value, never as text).
class SidebarCopyTest {

    private val english: Map<String, String> by lazy { readStrings("values") }
    private val dutch: Map<String, String> by lazy { readStrings("values-nl") }

    @Test
    fun the_sidebar_groups_carry_the_settled_names() {
        val expected: Map<String, String> =
            mapOf(
                "shell_group_home" to "Home",
                "shell_group_live" to "Live",
                "shell_group_build" to "Build",
                "shell_group_people" to "People",
                "shell_group_loyalty" to "Loyalty",
                "shell_group_music_voice" to "Music &amp; Voice",
                "shell_group_connect" to "Connect",
                "shell_group_setup" to "Setup",
            )

        for ((key: String, text: String) in expected) {
            assertEquals(text, english[key], "English text of $key")
            if (dutch[key].isNullOrBlank()) fail("Dutch text of $key is missing")
        }
    }

    @Test
    fun the_renamed_pages_carry_the_settled_names() {
        val expected: Map<String, String> =
            mapOf(
                "shell_nav_dashboard" to "Home",
                "shell_nav_rewards" to "Channel Points",
                "shell_nav_overlays" to "Overlays",
                "shell_nav_pick_lists" to "Random responses",
                "shell_nav_roles" to "Roles",
                "shell_nav_roles_individual" to "Individual permissions",
                "shell_nav_triggers_timers" to "Triggers &amp; Timers",
                "shell_nav_library" to "Library",
                "shell_nav_gallery" to "Gallery",
                "shell_nav_people" to "People",
                "shell_nav_code_scripts" to "Scripts",
                "shell_nav_custom_events" to "Custom Events",
                "shell_nav_automation" to "API &amp; devices",
                "shell_nav_stream" to "Stream",
            )

        for ((key: String, text: String) in expected) {
            assertEquals(text, english[key], "English text of $key")
            if (dutch[key].isNullOrBlank()) fail("Dutch text of $key is missing")
        }
    }

    @Test
    fun no_screen_text_says_super_mod() {
        val pattern = Regex("super[ _-]?mod", RegexOption.IGNORE_CASE)
        val hits: List<String> =
            (english.map { "values/${it.key}" to it.value } + dutch.map { "values-nl/${it.key}" to it.value })
                .filter { (_, text: String) -> pattern.containsMatchIn(text) }
                .map { it.first }

        if (hits.isNotEmpty()) fail("Screen text must say Lead Moderator, not Super Mod:\n" + hits.joinToString("\n"))
    }

    private fun readStrings(dir: String): Map<String, String> {
        val file = File(resourcesRoot(), "$dir/strings.xml")
        if (!file.isFile) fail("Missing $dir/strings.xml")

        return Regex("<string name=\"([^\"]+)\">(.*?)</string>", RegexOption.DOT_MATCHES_ALL)
            .findAll(file.readText())
            .associate { it.groupValues[1] to it.groupValues[2] }
    }

    private fun resourcesRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/composeResources")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate composeResources from ${System.getProperty("user.dir")}")
    }
}
