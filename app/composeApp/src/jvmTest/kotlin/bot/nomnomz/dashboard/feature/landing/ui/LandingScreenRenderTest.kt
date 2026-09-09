// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.landing.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import java.io.File
import kotlin.test.Test
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// Owner punch list 2026-09-08, §5: the logged-out home page used to be an icon, one heading line, one
// subtitle line, and a single "Get started" button — no feature descriptions, no reasons to use the bot.
// These tests prove the real replacement: (1) the hero + CTA are unchanged (no regression), (2) all seven
// feature groups render real, non-empty, English AND Dutch copy — not a crash-only smoke check, and
// (3) "Get started" is still the ONLY clickable element on the page, so it stays the page's one primary
// action (Sleak core rule 1) rather than competing with the new content for visual weight.
@OptIn(ExperimentalTestApi::class)
class LandingScreenRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun DutchContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "nl") {
            NomNomzTheme { content() }
        }
    }

    @Test
    fun the_hero_heading_subtitle_and_get_started_cta_still_render() = runComposeUiTest {
        setContent { EnglishContent { LandingScreen(onGetStarted = {}) } }
        waitForIdle()

        onNodeWithText("Your channel, supercharged").assertExists()
        onNodeWithText(
            "NomNomzBot runs your commands, timers, moderation, alerts, music, and more — one " +
                "dashboard for your whole stream.",
        ).assertExists()
        onNodeWithText("Get started").assertExists()
    }

    @Test
    fun all_seven_feature_groups_render_their_real_title_and_body_copy() = runComposeUiTest {
        setContent { EnglishContent { LandingScreen(onGetStarted = {}) } }
        waitForIdle()

        onNodeWithText("What you get").assertExists()

        onNodeWithText("One dashboard, every platform").assertExists()
        onNodeWithText(
            "Connect Twitch, Kick, YouTube, and X, and run one channel live across all of them " +
                "from a single dashboard.",
        ).assertExists()

        onNodeWithText("Commands, built visually").assertExists()
        onNodeWithText(
            "Build chat commands and automated responses with a visual pipeline editor — no " +
                "scripting required.",
        ).assertExists()

        onNodeWithText("Know your community, moderate with confidence").assertExists()
        onNodeWithText(
            "A viewer directory shows who's active in your community, and moderation tools " +
                "handle timeouts, bans, and rule enforcement.",
        ).assertExists()

        onNodeWithText("Text-to-speech that says it right").assertExists()
        onNodeWithText(
            "Give viewers a voice, pick from a library of voices, and set custom pronunciations " +
                "for the names and words your channel uses.",
        ).assertExists()

        onNodeWithText("Currency, games, and giveaways").assertExists()
        onNodeWithText(
            "Run a channel currency, play games, host giveaways, and track it all on a leaderboard.",
        ).assertExists()

        onNodeWithText("Spotify, Discord, and YouTube, connected").assertExists()
        onNodeWithText(
            "Share what's playing from Spotify, post updates to Discord, and bring in YouTube — " +
                "all from one dashboard.",
        ).assertExists()

        onNodeWithText("Overlays for OBS").assertExists()
        onNodeWithText(
            "Add browser-source overlays and widgets to OBS for alerts, now-playing, and more.",
        ).assertExists()
    }

    @Test
    fun dutch_locale_renders_dutch_feature_copy_not_english_fallback() = runComposeUiTest {
        setContent { DutchContent { LandingScreen(onGetStarted = {}) } }
        waitForIdle()

        onNodeWithText("Dit krijg je").assertExists()
        onNodeWithText("Eén dashboard, elk platform").assertExists()
        onNodeWithText("Commando's, visueel opgebouwd").assertExists()
        onNodeWithText("Ken je community, modereer met vertrouwen").assertExists()
        onNodeWithText("Tekst-naar-spraak die het goed uitspreekt").assertExists()
        onNodeWithText("Valuta, games en giveaways").assertExists()
        onNodeWithText("Spotify, Discord en YouTube, verbonden").assertExists()
        onNodeWithText("Overlays voor OBS").assertExists()
    }

    @Test
    fun get_started_is_the_only_clickable_element_on_the_page() = runComposeUiTest {
        setContent { EnglishContent { LandingScreen(onGetStarted = {}) } }
        waitForIdle()

        // Seven new feature blocks were added below the CTA; none of them may introduce a competing
        // clickable element (a button, a link) — the feature section is informational text inside plain
        // Cards. If this count ever exceeds 1, something in the features section grew a click target and
        // "Get started" stopped being the page's one primary action (Sleak core rule 1).
        onAllNodes(hasClickAction()).assertCountEquals(1)
        onNode(hasText("Get started") and hasClickAction()).assertExists()
    }

    @Test
    fun every_new_landing_feature_string_key_has_real_non_empty_copy_in_both_locales() {
        val newKeys: List<String> =
            listOf(
                "landing_features_heading",
                "landing_feature_platforms_title",
                "landing_feature_platforms_body",
                "landing_feature_pipelines_title",
                "landing_feature_pipelines_body",
                "landing_feature_community_title",
                "landing_feature_community_body",
                "landing_feature_tts_title",
                "landing_feature_tts_body",
                "landing_feature_economy_title",
                "landing_feature_economy_body",
                "landing_feature_integrations_title",
                "landing_feature_integrations_body",
                "landing_feature_widgets_title",
                "landing_feature_widgets_body",
            )

        val englishValues: Map<String, String> = stringEntries(localeStringsFile("values"))
        val dutchValues: Map<String, String> = stringEntries(localeStringsFile("values-nl"))
        val bannedPhrases: List<String> = listOf("coming soon", "lorem", "todo", "tbd", "placeholder")

        newKeys.forEach { key ->
            val english: String = englishValues[key] ?: fail("Missing English string resource: $key")
            val dutch: String = dutchValues[key] ?: fail("Missing Dutch string resource: $key")

            assertTrue(english.trim().isNotEmpty(), "English copy for $key must not be blank")
            assertTrue(dutch.trim().isNotEmpty(), "Dutch copy for $key must not be blank")
            // Real translated copy, not the English string duplicated into the nl file.
            assertNotEquals(english, dutch, "Dutch copy for $key must not be a copy of the English string")

            bannedPhrases.forEach { phrase ->
                assertTrue(
                    !english.lowercase().contains(phrase),
                    "English copy for $key must not use speculative/placeholder language ('$phrase'): $english",
                )
                assertTrue(
                    !dutch.lowercase().contains(phrase),
                    "Dutch copy for $key must not use speculative/placeholder language ('$phrase'): $dutch",
                )
            }
        }
    }

    private val stringEntry: Regex = Regex("""<string name="([^"]+)">(.*?)</string>""")

    private fun stringEntries(file: File): Map<String, String> =
        stringEntry.findAll(file.readText()).associate { it.groupValues[1] to it.groupValues[2] }

    // Same working-dir walk-up as StringResourceEscapingTest.resourcesRoot(): jvmTest may run from the
    // module dir or the repo root.
    private fun localeStringsFile(localeDir: String): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/composeResources/$localeDir/strings.xml")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate $localeDir/strings.xml from ${System.getProperty("user.dir")}")
    }
}
