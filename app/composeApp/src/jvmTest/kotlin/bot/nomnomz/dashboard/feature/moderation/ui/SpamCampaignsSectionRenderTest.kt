// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.SpamCampaign
import kotlin.test.Test

/**
 * S-MOD-REVERSAL-VISIBLE: a partially-reversed campaign must be legible on the dashboard, never
 * discoverable only in the database. Asserts the rendered semantics tree distinguishes all three
 * reversal states a campaign can be in: never attempted, partial, and fully reversed.
 */
@OptIn(ExperimentalTestApi::class)
class SpamCampaignsSectionRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    private fun baseCampaign(skeleton: String) = SpamCampaign(
        id = "campaign-$skeleton",
        skeleton = skeleton,
        verdict = "Campaign",
        qualificationCount = 10,
        actionableCount = 8,
        actionedCount = 8,
        noStandingShare = 0.9,
        mayContributeToNetwork = true,
        firstSeenAt = "2026-09-01T00:00:00Z",
        lastSeenAt = "2026-09-01T00:00:00Z",
    )

    @Test
    fun aCampaignNeverReversed_ShowsNeitherAReversedLineNorAPartialLine() {
        val neverAttempted = baseCampaign("never-attempted").copy(
            reversedAt = null,
            reversalReason = null,
            reversedByActorId = null,
            restoredAccountCount = 0,
            restorationFailedAccountIds = "",
        )

        runComposeUiTest {
            setContent { EnglishContent { SpamCampaignsSection(listOf(neverAttempted)) } }
            waitForIdle()

            onNodeWithText("Undone:", substring = true).assertDoesNotExist()
            onNodeWithText("Reversal incomplete", substring = true).assertDoesNotExist()
        }
    }

    @Test
    fun aPartiallyReversedCampaign_NamesHowManyAccountsAreStillActioned() {
        val partial = baseCampaign("partial-restore").copy(
            reversedAt = null,
            reversalReason = null,
            reversedByActorId = "system:spam-dequalify",
            restoredAccountCount = 1,
            restorationFailedAccountIds = "bot-d,bot-e",
        )

        runComposeUiTest {
            setContent { EnglishContent { SpamCampaignsSection(listOf(partial)) } }
            waitForIdle()

            // Names the shortfall by count — not a generic "reversal attempted" badge — and never
            // claims the completed-reversal wording.
            onNodeWithText("Reversal incomplete — 2 accounts still actioned", substring = true)
                .assertExists()
            onNodeWithText("Undone:", substring = true).assertDoesNotExist()
        }
    }

    @Test
    fun aFullyReversedCampaign_ShowsTheReversalReason_NeverThePartialWording() {
        val full = baseCampaign("fully-restored").copy(
            reversedAt = "2026-09-01T01:00:00Z",
            reversalReason = "Regulars joined the pattern.",
            reversedByActorId = "system:spam-dequalify",
            restoredAccountCount = 8,
            restorationFailedAccountIds = "",
        )

        runComposeUiTest {
            setContent { EnglishContent { SpamCampaignsSection(listOf(full)) } }
            waitForIdle()

            onNodeWithText("Undone: Regulars joined the pattern.", substring = true).assertExists()
            onNodeWithText("Reversal incomplete", substring = true).assertDoesNotExist()
        }
    }

    @Test
    fun allThreeStates_RenderSimultaneouslyWithDistinctText() {
        // The done-when: a partial state must be distinguishable from BOTH extremes on one screen, not
        // merely in isolation.
        val never = baseCampaign("never").copy(restorationFailedAccountIds = "")
        val partial = baseCampaign("partial").copy(
            reversedByActorId = "system:spam-dequalify",
            restoredAccountCount = 1,
            restorationFailedAccountIds = "bot-x",
        )
        val full = baseCampaign("full").copy(
            reversedAt = "2026-09-01T01:00:00Z",
            reversalReason = "Exonerated.",
            reversedByActorId = "system:spam-dequalify",
            restoredAccountCount = 8,
        )

        runComposeUiTest {
            setContent { EnglishContent { SpamCampaignsSection(listOf(never, partial, full)) } }
            waitForIdle()

            onNodeWithText("Reversal incomplete — 1 accounts still actioned", substring = true)
                .assertExists()
            onNodeWithText("Undone: Exonerated.", substring = true).assertExists()
        }
    }
}
