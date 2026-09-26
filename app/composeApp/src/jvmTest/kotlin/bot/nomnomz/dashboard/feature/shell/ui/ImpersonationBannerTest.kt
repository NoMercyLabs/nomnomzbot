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
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onNodeWithText
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
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.time.Duration.Companion.minutes

// The act-as banner is the ONE operator trace while acting as someone. Rendered from a real SessionStore: it names
// the impersonated user and the time left, offers Exit, stays up (with Exit) once the session has expired instead
// of vanishing, and is gone when nobody is being acted as. Both locales, one parameterised sentence each.
@OptIn(ExperimentalTestApi::class)
class ImpersonationBannerTest {

    // The banner collects the session with collectAsStateWithLifecycle(), which needs a resumed lifecycle owner.
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

    private fun acting(expiresAt: Instant): SessionStore =
        SessionStore(NoVault, NoProfile, NoChannel).apply {
            arm(ConnectionProfile(id = "p1", displayName = "Self-host", baseUrl = "http://localhost:5080", source = ProfileSource.Manual), SessionTokens(accessToken = "operator-jwt"))
            beginImpersonation("target-jwt", "Mod Mia", expiresAt, "grant-1")
        }

    @Test
    fun an_active_session_names_the_target_and_the_time_left_and_exit_fires() = runComposeUiTest {
        val store: SessionStore = acting(Clock.System.now() + 30.minutes)
        var exits = 0
        setContent { Pinned("en") { ImpersonationBanner(sessionStore = store, onExit = { exits++ }) } }

        onNodeWithText("Acting as Mod Mia · ends in", substring = true).assertExists()
        onNodeWithText("Stop impersonating").performClick()
        assertEquals(1, exits)
    }

    @Test
    fun an_expired_session_keeps_the_banner_and_its_exit() = runComposeUiTest {
        val store: SessionStore = acting(Clock.System.now() - 1.minutes)
        var exits = 0
        setContent { Pinned("en") { ImpersonationBanner(sessionStore = store, onExit = { exits++ }) } }

        onNodeWithText("The session acting as Mod Mia has ended. Exit to return to your own account.").assertExists()
        onNodeWithText("Stop impersonating").performClick()
        assertEquals(1, exits)
    }

    @Test
    fun the_banner_renders_in_dutch() = runComposeUiTest {
        val store: SessionStore = acting(Clock.System.now() + 30.minutes)
        setContent { Pinned("nl") { ImpersonationBanner(sessionStore = store, onExit = {}) } }

        onNodeWithText("Handelt als Mod Mia · eindigt over", substring = true).assertExists()
        onNodeWithText("Stoppen met imiteren").assertExists()
    }

    @Test
    fun nothing_renders_when_nobody_is_being_acted_as() = runComposeUiTest {
        val store = SessionStore(NoVault, NoProfile, NoChannel)
        setContent { Pinned("en") { ImpersonationBanner(sessionStore = store, onExit = {}) } }

        onAllNodesWithText("Stop impersonating").assertCountEquals(0)
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
