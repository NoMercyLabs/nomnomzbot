// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.fail

// The server pushes changes over the hub, so a feature screen must not ask again every few seconds. This guard
// scans every file under .../dashboard/feature/ for a `while (true)` / `while (isActive)` loop whose body calls
// `delay(` (the poll-and-reload shape) and fails on any file not in [allowed]. The scan is structural (a balanced
// brace walk over each loop), never a hand-typed list of the offenders.
//
// [allowed] may only shrink. Raising it to make a new failure go away defeats the guard.
class NoPollingLoopGuardTest {

    private val allowed: Map<String, String> =
        mapOf(
            // The hub-less fallback loop: it runs only when no hub flow is passed (a test/local build).
            "chat/ui/ChatScreen.kt" to "fallback poll when no hub is connected",
            // A one-second countdown clock to the pairing code's expiry; it reads no server data.
            "automation/ui/AutomationScreen.kt" to "countdown clock, no load",
            // Known pollers. Each is to be removed when its screen follows a hub push.
            "games/ui/GamesScreen.kt" to "known poller, to be removed",
            "rewards/ui/RewardsScreen.kt" to "known poller, to be removed",
            "shell/ui/ReauthDialog.kt" to "known poller (Twitch health re-probe), to be removed",
        )

    @Test
    fun no_feature_file_polls_with_a_delay_loop_unless_allowlisted() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace(File.separatorChar, '/')
            if (rel in allowed) return@forEach
            if (hasPollingLoop(file.readText())) offenders.add(rel)
        }
        if (offenders.isNotEmpty()) {
            fail(
                "Polling loop (while + delay) found in: ${offenders.sorted()}. Follow a hub push instead " +
                    "(see ChatPollsController.subscribeToHub), or justify an allowlist entry.",
            )
        }
    }

    @Test
    fun the_scanner_flags_a_poll_and_load_loop_and_passes_a_countdown_free_body() {
        val polling: String = "LaunchedEffect(Unit) {\n  while (true) {\n    delay(4000)\n    controller.load()\n  }\n}"
        val noDelay: String = "awaitPointerEventScope {\n  while (true) {\n    awaitPointerEvent()\n  }\n}"
        assertTrue(hasPollingLoop(polling))
        assertTrue(!hasPollingLoop(noDelay))
    }

    @Test
    fun every_allowlisted_file_still_exists_and_still_polls() {
        val root: File = featureRoot()
        val stale: List<String> =
            allowed.keys.filter { rel ->
                val file = File(root, rel)
                !file.isFile || !hasPollingLoop(file.readText())
            }
        if (stale.isNotEmpty()) fail("Allowlist entries with no polling loop left, remove them: $stale")
    }

    private fun hasPollingLoop(source: String): Boolean {
        val loopStart: Regex = Regex("""while\s*\(\s*(true|isActive)\s*\)\s*\{""")
        return loopStart.findAll(source).any { match ->
            var depth: Int = 1
            var i: Int = match.range.last + 1
            while (i < source.length && depth > 0) {
                when (source[i]) {
                    '{' -> depth++
                    '}' -> depth--
                }
                i++
            }
            source.substring(match.range.last + 1, i).contains("delay(")
        }
    }

    private fun featureRoot(): File {
        val relative: String = "src/commonMain/kotlin/bot/nomnomz/dashboard/feature"
        val candidates: List<File> =
            listOf(File("composeApp/$relative"), File(relative), File("app/composeApp/$relative"))
        return candidates.firstOrNull { it.isDirectory } ?: fail("feature tree not found from ${File(".").absolutePath}")
    }
}
