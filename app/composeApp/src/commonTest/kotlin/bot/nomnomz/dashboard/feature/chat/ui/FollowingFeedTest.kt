// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performMouseInput
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.waitUntilAtLeastOneExists
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.test.swipeDown
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S-UF-M3: a moderator aiming at a line must not lose it to the newest message. Drives the shared
// [FollowingFeed] (Chat and Multi-Chat both host their lines in it) with a real pointer and real scrolls.
@OptIn(ExperimentalTestApi::class)
class FollowingFeedTest {

    private fun ComposeUiTest.visibleLines(): List<String> =
        onAllNodes(hasText("line ", substring = true))
            .fetchSemanticsNodes()
            .mapNotNull { node -> node.config.getOrNull(SemanticsProperties.Text)?.firstOrNull()?.text }

    private fun ComposeUiTest.host(
        lines: List<String>,
        follow: FeedFollowState,
    ): () -> Unit {
        var current: List<String> by mutableStateOf(lines)
        setContent {
            NomNomzTheme {
                FollowingFeed(
                    items = current,
                    key = { _, line -> line },
                    followState = follow,
                    modifier = Modifier.testTag("feed").height(200.dp),
                ) { line -> Text(line) }
            }
        }
        return { current = current + ((current.size + 1)..(current.size + 27)).map { n -> "line $n" } }
    }

    private fun initial(): List<String> = (1..30).map { n -> "line $n" }

    @Test
    fun pointer_over_the_feed_keeps_the_lines_still_and_the_pill_counts_new_messages_until_clicked() = runComposeUiTest {
        val follow = FeedFollowState(clock = { mainClock.currentTime })
        val append: () -> Unit = host(initial(), follow)
        waitForIdle()
        assertTrue("line 30" in visibleLines(), "starts on the newest line")

        onNodeWithTag("feed").performMouseInput { enter(center) }
        val before: List<String> = visibleLines()
        append()
        waitForIdle()

        assertEquals(before, visibleLines(), "the lines under the pointer did not move")
        waitUntilAtLeastOneExists(pill, 5_000)

        onNode(pill).performClick()
        waitForIdle()
        assertTrue("line 57" in visibleLines(), "the pill shows the last line: ${visibleLines()}")
        assertEquals(0, onAllNodesWithPillText().size, "the pill is gone once caught up")
    }

    @Test
    fun following_resumes_three_seconds_after_the_pointer_leaves() = runComposeUiTest {
        val follow = FeedFollowState(clock = { mainClock.currentTime })
        val append: () -> Unit = host(initial(), follow)
        waitForIdle()
        onNodeWithTag("feed").performMouseInput { enter(center) }
        append()
        waitForIdle()

        onNodeWithTag("feed").performMouseInput { exit(Offset(-50f, -50f)) }
        mainClock.advanceTimeBy(2_900)
        waitForIdle()
        assertTrue("line 57" !in visibleLines(), "still paused inside the 3 s grace period")

        mainClock.advanceTimeBy(200)
        waitForIdle()
        assertTrue("line 57" in visibleLines(), "following resumed: ${visibleLines()}")
        assertEquals(0, onAllNodesWithPillText().size)
    }

    @Test
    fun a_scroll_up_stops_following_and_it_does_not_resume_when_the_pointer_is_gone() = runComposeUiTest {
        val follow = FeedFollowState(clock = { mainClock.currentTime })
        val append: () -> Unit = host(initial(), follow)
        waitForIdle()

        onNodeWithTag("feed").performTouchInput { swipeDown() }
        waitForIdle()
        val before: List<String> = visibleLines()
        assertTrue("line 30" !in before, "the swipe moved toward older lines: $before")

        append()
        mainClock.advanceTimeBy(10_000)
        waitForIdle()

        assertEquals(before, visibleLines(), "still reading the older lines")
        waitUntilAtLeastOneExists(pill, 5_000)
    }

    // Audit C7a1-28: ChatController appends blank-id lines on purpose, and a replayed line can repeat an id. The feed
    // itself must keep every line, never crash the page on a duplicate LazyColumn key.
    @Test
    fun lines_with_blank_ids_are_all_shown_and_do_not_crash() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                FollowingFeed(
                    items = listOf("line a", "line b", "line c"),
                    key = { _, _ -> "" },
                    modifier = Modifier.height(200.dp),
                ) { line -> Text(line) }
            }
        }
        waitForIdle()
        assertEquals(listOf("line a", "line b", "line c"), visibleLines())
    }

    @Test
    fun lines_with_equal_ids_are_all_shown_and_do_not_crash() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                FollowingFeed(
                    items = listOf("line a", "line b", "line c"),
                    key = { _, line -> if (line == "line c") "c" else "same" },
                    modifier = Modifier.height(200.dp),
                ) { line -> Text(line) }
            }
        }
        waitForIdle()
        assertEquals(listOf("line a", "line b", "line c"), visibleLines())
    }

    // The pill is the feed's only clickable node. Matched by its count, not its words, so the test is locale-proof
    // (the test JVM's locale picks en or nl): "27 new messages" / "27 nieuwe berichten".
    private val pill: SemanticsMatcher = hasClickAction() and hasText("27", substring = true)

    private fun ComposeUiTest.onAllNodesWithPillText() = onAllNodes(hasClickAction()).fetchSemanticsNodes()
}
