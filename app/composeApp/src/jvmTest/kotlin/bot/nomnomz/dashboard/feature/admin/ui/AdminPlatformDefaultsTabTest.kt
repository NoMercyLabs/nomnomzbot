// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ActionDangerTier
import bot.nomnomz.dashboard.core.network.ActionDefault
import bot.nomnomz.dashboard.feature.admin.state.FakePlatformDefaultsApi
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * The rendered platform-defaults tab (plan item A4): rows name ROLES (never numbered levels), the editor shows
 * the server's counted blast radius BEFORE its apply button does anything, applying sends exactly the
 * previewed count, and the row then shows the server's read-back. A dangerous action's apply stays disabled
 * until the acknowledgement is ticked.
 */
@OptIn(ExperimentalTestApi::class)
class AdminPlatformDefaultsTabTest {

    @Composable
    private fun English(content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = "en") { NomNomzTheme { content() } }
        }
    }

    private val commandsWrite = ActionDefault(
        actionKey = "commands:write",
        description = "Manage custom commands",
        shippedDefaultLevel = 10,
        effectiveDefaultLevel = 10,
        floorLevel = 10,
        channelOverrideCount = 1,
    )

    @Test
    fun rows_show_role_names_and_the_override_count_never_a_number() {
        val controller = PlatformDefaultsController(FakePlatformDefaultsApi(listOf(commandsWrite)))
        runTest { controller.loadActionDefaults() }

        runComposeUiTest {
            setContent { English { PlatformDefaultsTab(controller) } }
            waitForIdle()

            onNodeWithText("Default: Moderator").assertExists()
            onNodeWithText("1 channel(s) set their own level").assertExists()
            onNodeWithText("Default: 10").assertDoesNotExist()
        }
    }

    @Test
    fun the_counted_blast_radius_is_on_screen_before_apply_and_apply_sends_that_count() {
        val api = FakePlatformDefaultsApi(listOf(commandsWrite))
        val controller = PlatformDefaultsController(api)
        runTest {
            controller.loadActionDefaults()
            controller.pickActionLevel("commands:write", 30)
        }

        runComposeUiTest {
            setContent { English { PlatformDefaultsTab(controller) } }
            waitForIdle()

            onNodeWithText("This changes 3 channel(s) right now, including: alpha, bravo.").assertExists()
            onNodeWithText("1 channel(s) keep their own setting.").assertExists()
            assertTrue(api.saves.isEmpty(), "showing the editor must not save anything")

            onNode(hasText("Apply to 3 channel(s)") and hasClickAction()).performClick()
            waitForIdle()

            assertEquals(3, api.saves.single().second.confirmedChannelsAffected)
            assertEquals(30, api.saves.single().second.level)
            onNodeWithText("Default: Editor").assertExists()
            onNodeWithText("Shipped default: Moderator").assertExists()
        }
    }

    @Test
    fun a_dangerous_action_keeps_apply_disabled_until_acknowledged() {
        val nuke = commandsWrite.copy(actionKey = "moderation:nuke", floorTier = ActionDangerTier.CRITICAL)
        val api = FakePlatformDefaultsApi(listOf(nuke))
        val controller = PlatformDefaultsController(api)
        runTest {
            controller.loadActionDefaults()
            controller.pickActionLevel("moderation:nuke", 30)
        }

        runComposeUiTest {
            setContent { English { PlatformDefaultsTab(controller) } }
            waitForIdle()

            onNode(hasText("Apply to 3 channel(s)") and hasClickAction()).assertIsNotEnabled()
            onNodeWithText(
                "I understand this guards a dangerous action and the change applies to every channel above.",
            ).assertExists()
            controller.acknowledgeDanger(true)
            waitForIdle()
            onNode(hasText("Apply to 3 channel(s)") and hasClickAction()).assertIsEnabled()
            assertTrue(api.saves.isEmpty())
        }
    }
}
