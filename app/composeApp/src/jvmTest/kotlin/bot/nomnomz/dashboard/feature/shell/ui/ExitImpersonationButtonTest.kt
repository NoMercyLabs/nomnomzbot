// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.connection.ActiveChannelStore
import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlinx.datetime.Clock
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.time.Duration.Companion.minutes

// While acting, the Exit button is the ONE operator trace on screen. Rendered from a real SessionStore: it reads
// "Exit impersonation" (both locales) and nothing else — no operator name, no target name, no timer; a press fires
// the exit exactly once (a second press while the exit runs is ignored); and it is gone when nobody is being acted
// as, including the moment an act-as session ends.
@OptIn(ExperimentalTestApi::class)
class ExitImpersonationButtonTest {

    // The button collects the session with collectAsStateWithLifecycle(), which needs a resumed lifecycle owner.
    @Composable
    private fun Pinned(tag: String, content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).currentState = Lifecycle.State.RESUMED
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = tag) { NomNomzTheme { content() } }
        }
    }

    // The operator (Stoney_Eagle) signed in, then acting as anda_six.
    private fun acting(): SessionStore =
        SessionStore(NoVault, NoProfile, NoChannel).apply {
            arm(
                ConnectionProfile(id = "p1", displayName = "Self-host", baseUrl = "http://localhost:5080", source = ProfileSource.Manual),
                SessionTokens(accessToken = "operator-jwt"),
            )
            setUser(SessionUser("operator-id", "stoney_eagle", "Stoney_Eagle", null, isAdmin = true))
            beginImpersonation("target-jwt", "anda_six", Clock.System.now() + 30.minutes, "grant-1")
        }

    @Test
    fun while_acting_only_the_exit_control_renders_and_one_press_fires_one_exit() = runComposeUiTest {
        val store: SessionStore = acting()
        var exits = 0
        setContent { Pinned("en") { ExitImpersonationButton(sessionStore = store, onExit = { exits++ }) } }

        onAllNodesWithText("Exit impersonation", useUnmergedTree = true).assertCountEquals(1)
        // No name of either account and no countdown: the control says only what it does.
        onAllNodesWithText("Stoney_Eagle", substring = true, useUnmergedTree = true).assertCountEquals(0)
        onAllNodesWithText("anda_six", substring = true, useUnmergedTree = true).assertCountEquals(0)
        onAllNodesWithText("min", substring = true, useUnmergedTree = true).assertCountEquals(0)

        onNodeWithText("Exit impersonation").performClick()
        // The exit is running: a second press must not queue a second exit.
        onRoot().performClick()
        waitForIdle()
        assertEquals(1, exits)
    }

    @Test
    fun the_exit_control_renders_in_dutch() = runComposeUiTest {
        val store: SessionStore = acting()
        setContent { Pinned("nl") { ExitImpersonationButton(sessionStore = store, onExit = {}) } }

        onAllNodesWithText("Imitatie beëindigen", useUnmergedTree = true).assertCountEquals(1)
        onAllNodesWithText("Exit impersonation", useUnmergedTree = true).assertCountEquals(0)
    }

    @Test
    fun nothing_renders_when_nobody_is_being_acted_as() = runComposeUiTest {
        val store = SessionStore(NoVault, NoProfile, NoChannel)
        setContent { Pinned("en") { ExitImpersonationButton(sessionStore = store, onExit = {}) } }

        onAllNodesWithText("Exit impersonation", useUnmergedTree = true).assertCountEquals(0)
    }

    @Test
    fun the_control_leaves_the_moment_the_act_as_session_ends() = runComposeUiTest {
        val store: SessionStore = acting()
        setContent { Pinned("en") { ExitImpersonationButton(sessionStore = store, onExit = {}) } }
        onAllNodesWithText("Exit impersonation", useUnmergedTree = true).assertCountEquals(1)

        store.endImpersonation()
        waitForIdle()

        onAllNodesWithText("Exit impersonation", useUnmergedTree = true).assertCountEquals(0)
    }

    private object NoVault : SessionTokenStore {
        override suspend fun read(profileId: String): SessionTokens? = null
        override suspend fun write(profileId: String, tokens: SessionTokens) = Unit
        override suspend fun clear(profileId: String) = Unit
    }

    private object NoProfile : ActiveProfileStore {
        override suspend fun read(): ConnectionProfile? = null
        override suspend fun write(profile: ConnectionProfile) = Unit
        override suspend fun clear() = Unit
    }

    private object NoChannel : ActiveChannelStore {
        override suspend fun read(): String? = null
        override suspend fun write(channelId: String) = Unit
        override suspend fun clear() = Unit
    }
}
