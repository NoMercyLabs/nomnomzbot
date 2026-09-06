// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// S-UI-TRUNCATION: at 390 dp a pipeline's name rendered as "Raid…" / "Pijplij…" — the SAME
// `maxLines = 1` + `TextOverflow.Ellipsis` shape the owner already paid for once (see
// [RowLabelGuardTest]'s "some ui rows have no name but do have values and actions" report) turned up
// again, this time clipping a row's PRIMARY identifying label instead of leaving it blank. A grep
// found ~40 more feature screens carrying the same pattern.
//
// Not every `maxLines = 1` + Ellipsis site is a defect — a chat message preview, a byline, a status
// line, or a URL shown for reference may legitimately clip. So this guard does not ban the pattern
// outright; it counts every occurrence STRUCTURALLY (a balanced-paren scan over every `Text(...)` /
// `EmojiText(...)` call in the feature tree, never a hand-typed list of files) and pins the count each
// file is allowed via [secondaryClipBaseline] — every number below was re-derived from the ACTUAL
// tree on 2026-09-06, after every PRIMARY row label found in this pass was fixed (113 sites across 50
// screens, by letting the label wrap or, where the row's layout is genuinely tight, capping it at
// `maxLines = 2` with Ellipsis kept only as a fallback for the rare name long enough to still overflow
// two lines). What remains here was hand-verified to be secondary/preview text — a status line, a
// byline, a URL, an event-type summary, a section heading — never a row's own name/title.
//
// A file's count going ABOVE its baseline means either a fresh primary-label clip was introduced, or a
// new secondary site needs a human's sign-off — both are supposed to fail loudly rather than drift.
// The baseline may only shrink (a screen fixed to wrap even its secondary text, or restructured so the
// site disappears) — raising a number back up defeats the guard the same way a permanent allowlist
// would, so don't do it to make a new failure go away.
class TruncatedPrimaryLabelGuardTest {

    // rel path (from .../dashboard/feature/) -> allowed maxLines=1+Ellipsis call count.
    private val secondaryClipBaseline: Map<String, Int> =
        mapOf(
            "admin/ui/AdminScreen.kt" to 1,
            "alerts/ui/AlertsScreen.kt" to 1,
            "assets/ui/AssetsScreen.kt" to 1,
            "chat/ui/ChatScreen.kt" to 3,
            "chat/ui/EmoteComposerField.kt" to 1,
            "chatpolls/ui/ChatPollsCard.kt" to 1,
            "chattriggers/ui/ChatTriggersScreen.kt" to 2,
            "commands/ui/CommandsScreen.kt" to 1,
            "customevents/ui/CustomEventsScreen.kt" to 2,
            "discord/ui/DiscordScreen.kt" to 2,
            "economy/ui/EconomyScreen.kt" to 11,
            "federation/ui/FederationScreen.kt" to 2,
            "games/ui/GamesScreen.kt" to 3,
            "giveaways/ui/GiveawaysScreen.kt" to 1,
            "home/ui/AttentionInbox.kt" to 1,
            "home/ui/HomeScreen.kt" to 2,
            "liveops/ui/ScheduleScreen.kt" to 1,
            "mediashare/ui/MediaShareScreen.kt" to 1,
            "moderation/ui/ModerationScreen.kt" to 6,
            "music/ui/MusicScreen.kt" to 7,
            "participant/ui/ParticipantShell.kt" to 1,
            // Fixed in a separate, already-committed slice (tree-editor containment/depth); this is its
            // own remaining secondary count, tracked here rather than left to drift unnoticed.
            "pipelines/ui/PipelinesScreen.kt" to 13,
            "quotes/ui/QuotesScreen.kt" to 1,
            "rewards/ui/RewardsScreen.kt" to 4,
            "roles/ui/RolesScreen.kt" to 2,
            "settings/ui/SettingsScreen.kt" to 2,
            "shell/ui/ShellScreen.kt" to 1,
            "songrequests/ui/SongRequestsScreen.kt" to 2,
            "sound/ui/SoundScreen.kt" to 1,
            "supporters/ui/SupportersScreen.kt" to 1,
            "tts/ui/TtsScreen.kt" to 1,
            "webhooks/ui/WebhooksScreen.kt" to 5,
            "widgets/ui/WidgetsScreen.kt" to 3,
        )

    private val callStart: Regex = Regex("""\b(Text|EmojiText)\(""")
    private val maxLinesOne: Regex = Regex("""maxLines\s*=\s*1\b""")

    /** Counts `Text(...)` / `EmojiText(...)` calls whose OWN argument list carries both `maxLines = 1`
     * and `TextOverflow.Ellipsis` — a balanced-paren scan, so a `maxLines = 1` on one call can never be
     * paired with an `Ellipsis` belonging to a different, nearby call. */
    private fun countSingleLineEllipsisCalls(source: String): Int {
        var count = 0
        var searchFrom = 0
        while (true) {
            val match = callStart.find(source, searchFrom) ?: break
            var depth = 1
            var i = match.range.last + 1
            while (i < source.length && depth > 0) {
                when (source[i]) {
                    '(' -> depth++
                    ')' -> depth--
                }
                i++
            }
            val block: String = source.substring(match.range.first, i)
            if (maxLinesOne.containsMatchIn(block) && block.contains("TextOverflow.Ellipsis")) {
                count++
            }
            searchFrom = if (i > match.range.first) i else match.range.last + 1
        }
        return count
    }

    @Test
    fun no_row_ships_a_new_single_line_ellipsis_clip_beyond_the_tracked_secondary_baseline() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()

        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val count: Int = countSingleLineEllipsisCalls(file.readText())
            val allowed: Int = secondaryClipBaseline[rel] ?: 0
            if (count > allowed) {
                offenders += "$rel: $count maxLines=1+Ellipsis call(s), baseline allows $allowed"
            }
        }

        if (offenders.isNotEmpty()) {
            fail(
                "New `maxLines = 1` + `TextOverflow.Ellipsis` site(s) beyond the tracked baseline. If this " +
                    "is the row's PRIMARY identifying label (the name/title next to its actions), fix it — let " +
                    "it wrap, or use `maxLines = 2` with Ellipsis only as a fallback — never raise the baseline " +
                    "to silence this. If it is genuinely secondary/preview text (a byline, a status line, a " +
                    "URL), add it to secondaryClipBaseline explicitly, it is never auto-allowed.\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    private fun featureRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate =
                File(dir, "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate feature source from ${System.getProperty("user.dir")}")
    }
}
