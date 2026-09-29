// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.PipelineSummary
import kotlin.test.Test
import kotlin.test.assertEquals

// The pipelines list row offers "Restore default" only on a pipeline that has a platform default (a seeded raid
// flow); a pipeline the channel built itself has nothing to go back to, so it shows no such action.
@OptIn(ExperimentalTestApi::class)
class PipelineRowRestoreDefaultTest {
    @Test
    fun a_seeded_pipeline_offers_restore_and_a_channel_pipeline_does_not() = runComposeUiTest {
        var restoreClicks = 0

        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    Column {
                        PipelineRow(
                            pipeline = PipelineSummary(id = "raid", name = "Raid", hasPlatformDefault = true),
                            manage = ManageDecision.Allowed,
                            onOpen = {},
                            onEdit = {},
                            onToggle = {},
                            onDelete = {},
                            onRestoreDefault = { restoreClicks++ },
                        )
                        PipelineRow(
                            pipeline = PipelineSummary(id = "mine", name = "Mine"),
                            manage = ManageDecision.Allowed,
                            onOpen = {},
                            onEdit = {},
                            onToggle = {},
                            onDelete = {},
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Restore Mine to the platform default").assertDoesNotExist()
        onNodeWithContentDescription("Restore Raid to the platform default").performClick()
        waitForIdle()

        assertEquals(1, restoreClicks)
    }
}
