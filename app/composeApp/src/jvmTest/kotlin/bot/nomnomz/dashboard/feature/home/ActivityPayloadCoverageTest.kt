// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

/**
 * The recent-activity feed rendered bare labels and threw away every number the backend recorded:
 * "QTkittE resubscribed" with no month count, a gift with no count, a redemption with no cost, a timeout
 * with no duration. Each was fixed one at a time, only ever after the owner spotted it in his own chat —
 * which is the failure this guard exists to end.
 *
 * It is STRUCTURAL, not a hand-maintained list: it reads the backend projection that decides what goes
 * into an event payload, extracts every numeric/boolean field it records, and asserts the feed actually
 * reads each one. Adding a new field to the projection fails this test until the feed shows it, so the
 * next figure cannot silently go missing.
 */
class ActivityPayloadCoverageTest {

    /** Payload keys that are identity/plumbing rather than something a feed row would state. */
    private val notDisplayed: Set<String> =
        setOf(
            // Ids and logins — the row already shows the display name.
            "userId",
            "userLogin",
            "userDisplayName",
            "fromUserId",
            "fromLogin",
            "fromDisplayName",
            "gifterUserId",
            "gifterDisplayName",
            "targetUserId",
            "targetDisplayName",
            "moderatorUserId",
            "rewardId",
            "redemptionId",
            // Rendered, but through its own dedicated reader rather than the numeric one.
            "rewardTitle",
            // A sub that ENDED is not shown in the feed at all, so its gift flag has nothing to render into.
            "isGift",
        )

    @Test
    fun `every figure the backend records on a feed event is actually shown`() {
        val projection: File = repoFile(
            "server/src/NomNomzBot.Infrastructure/Analytics/TwitchChannelEventLogProjection.cs"
        )
        val screen: File = repoFile(
            "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature/home/ui/HomeScreen.kt"
        )

        val projectionSource: String = projection.readText()
        val screenSource: String = screen.readText()

        // CopyInt/CopyBool record the FIGURES — the ("EntityField", "payloadKey") pairs the feed can show.
        val figureBlocks: List<String> =
            Regex("""Copy(?:Int|Bool)\s*\((.*?)\);""", RegexOption.DOT_MATCHES_ALL)
                .findAll(projectionSource)
                .map { it.groupValues[1] }
                .toList()

        val payloadKeys: Set<String> =
            figureBlocks
                .flatMap { Regex(""""(\w+)"\s*\)""").findAll(it).map { m -> m.groupValues[1] } }
                .toSet() - notDisplayed

        if (payloadKeys.isEmpty()) {
            fail(
                "Extracted no payload figures from ${projection.name} — the projection's shape changed and " +
                    "this guard is now blind. Fix the extraction rather than deleting the test."
            )
        }

        val unread: List<String> = payloadKeys.filter { !screenSource.contains("\"$it\"") }.sorted()

        if (unread.isNotEmpty()) {
            fail(
                "The activity feed records these figures but never shows them: ${unread.joinToString()}.\n" +
                    "Each one is a row that states an event happened while hiding the number the streamer " +
                    "reads the feed for. Render it in HomeScreen.kt, or add it to `notDisplayed` with a " +
                    "reason if it genuinely is not a figure a row would state."
            )
        }
    }

    private fun repoFile(relative: String): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, relative)
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate $relative from ${System.getProperty("user.dir")}")
    }
}
