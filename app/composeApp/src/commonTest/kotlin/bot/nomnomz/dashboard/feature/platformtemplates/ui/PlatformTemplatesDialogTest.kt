// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformtemplates.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.InstalledPlatformTemplate
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PlatformTemplate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the generic install dialog's pipeline rules and failure path, independent of any one feature page:
// an Optional binding installs without a pipeline or with the chosen one, and a refused install keeps the
// dialog open with the server's reason instead of reporting success.
@OptIn(ExperimentalTestApi::class)
class PlatformTemplatesDialogTest {

    private val template: PlatformTemplate =
        PlatformTemplate(
            definitionId = "def-1",
            kind = "timer",
            key = "hydrate",
            displayName = "Hydrate",
            version = 1,
            payloadJson = "{}",
        )

    @Test
    fun an_optional_pipeline_installs_without_one_or_with_the_chosen_one() = runComposeUiTest {
        val installs: MutableList<String?> = mutableListOf()
        var installed: InstalledPlatformTemplate? = null
        setContent {
            NomNomzTheme {
                bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                    PlatformTemplatesDialog(
                        loadTemplates = { ApiResult.Ok(listOf(template)) },
                        install = { _, pipelineId ->
                            installs += pipelineId
                            ApiResult.Ok(InstalledPlatformTemplate(kind = "timer", entityId = "t1", name = "Hydrate"))
                        },
                        onInstalled = { installed = it },
                        onDismiss = {},
                        consequence = { null },
                        pipelines = listOf(PipelineSummary(id = "pipe-9", name = "Shoutout flow")),
                        pipelineUse = { TemplatePipelineUse.Optional },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Pipeline to run (optional)").assertExists()
        onNodeWithText("Install").assertIsEnabled().performClick()
        waitForIdle()

        assertEquals(listOf<String?>(null), installs)
        assertEquals("t1", installed?.entityId)
    }

    @Test
    fun a_refused_install_shows_the_reason_and_does_not_report_installed() = runComposeUiTest {
        var installed: InstalledPlatformTemplate? = null
        setContent {
            NomNomzTheme {
                bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                    PlatformTemplatesDialog(
                        loadTemplates = { ApiResult.Ok(listOf(template)) },
                        install = { _, _ ->
                            ApiResult.Failure(ApiError(status = 409, code = "ALREADY_EXISTS", message = "Title already used"))
                        },
                        onInstalled = { installed = it },
                        onDismiss = {},
                        consequence = { null },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Install").performClick()
        waitForIdle()

        onNodeWithText("Title already used").assertExists()
        assertNull(installed)
    }
}
