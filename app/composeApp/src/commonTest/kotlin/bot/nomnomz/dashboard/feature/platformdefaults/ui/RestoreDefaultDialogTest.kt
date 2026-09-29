// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.platformdefaults.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PlatformDefaultChange
import bot.nomnomz.dashboard.core.network.PlatformDefaultPreview
import bot.nomnomz.dashboard.feature.platformdefaults.state.RestoreDefaultController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// "Restore default" as rendered: the dialog names every part it replaces (current → default) before anything is
// written, only the confirm restores, and a row that already matches cannot be confirmed.
@OptIn(ExperimentalTestApi::class)
class RestoreDefaultDialogTest {
    private val edited: PlatformDefaultPreview =
        PlatformDefaultPreview(
            kind = "timer",
            defaultName = "Hydrate",
            installedVersion = 1,
            defaultVersion = 2,
            isEdited = true,
            changes =
                listOf(
                    PlatformDefaultChange("interval", "10", "45"),
                    PlatformDefaultChange("enabled", "false", "true"),
                ),
        )

    @Test
    fun it_names_what_goes_back_and_only_the_confirm_restores() = runComposeUiTest {
        var restores = 0
        var restored = false
        val controller =
            RestoreDefaultController(
                load = { ApiResult.Ok(edited) },
                restore = {
                    restores++
                    ApiResult.Ok(edited.copy(isEdited = false, changes = emptyList()))
                },
            )

        setContent { host { RestoreDefaultDialog("Water", controller, onRestored = { restored = true }, onDismiss = {}) } }
        waitForIdle()

        onNodeWithText("These go back to the platform default (version 2):").assertExists()
        onNodeWithText("Interval (minutes): 10 → 45").assertExists()
        onNodeWithText("On or off: Off → On").assertExists()
        assertEquals(0, restores, "nothing is restored before the confirm")

        onNodeWithTag("restore-default-confirm").performClick()
        waitForIdle()

        assertEquals(1, restores)
        assertTrue(restored, "the caller hears the restore landed so it can reload")
    }

    @Test
    fun a_row_that_already_matches_cannot_be_confirmed() = runComposeUiTest {
        val controller =
            RestoreDefaultController(
                load = { ApiResult.Ok(edited.copy(isEdited = false, changes = emptyList())) },
                restore = { error("a restore with nothing to change must never be sent") },
            )

        setContent { host { RestoreDefaultDialog("Hydrate", controller, onRestored = {}, onDismiss = {}) } }
        waitForIdle()

        onNodeWithText("This already matches the platform default. There is nothing to restore.").assertExists()
        onNodeWithTag("restore-default-confirm").assertIsNotEnabled()
    }
}

@Composable
private fun host(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).currentState = Lifecycle.State.RESUMED
    CompositionLocalProvider(LocalLifecycleOwner provides owner) {
        NomNomzTheme { AppEnvironment("en") { content() } }
    }
}
