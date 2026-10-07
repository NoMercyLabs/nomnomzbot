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

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.component.LoadFailedRetryTag
import bot.nomnomz.dashboard.core.designsystem.component.LoadFailedStateTag
import bot.nomnomz.dashboard.core.designsystem.component.NoResultsState
import bot.nomnomz.dashboard.core.designsystem.component.SkeletonLoadingTag
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.PlatformTemplatesApi
import bot.nomnomz.dashboard.core.network.QuotePage
import bot.nomnomz.dashboard.core.network.QuotesApi
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.feature.commands.state.BuiltinDetailController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesController
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import bot.nomnomz.dashboard.feature.commands.ui.CommandsScreen
import bot.nomnomz.dashboard.feature.quotes.state.QuotesController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.quotes.ui.QuotesScreen
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.timers.ui.TimersScreen
import java.lang.reflect.Proxy
import java.util.concurrent.atomic.AtomicInteger
import kotlin.coroutines.intrinsics.COROUTINE_SUSPENDED
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * X3 (psychology spec): a failed load shows the shared failed-load state with a Retry that loads again, never an
 * empty list; and a pending load shows the shared skeleton, never a bare text line. The API fakes are dynamic
 * proxies: every call the screen's first load makes is answered by [answer], anything else fails loudly.
 */
@OptIn(ExperimentalTestApi::class)
class LoadStatesScreensTest {
    private val failureMessage: String = "backend is down"

    private inline fun <reified T : Any> fake(noinline answer: (method: String) -> Any?): T =
        Proxy.newProxyInstance(T::class.java.classLoader, arrayOf(T::class.java)) { proxy, method, args ->
            when (method.name) {
                "equals" -> proxy === args?.firstOrNull()
                "hashCode" -> System.identityHashCode(proxy)
                "toString" -> "fake<${T::class.java.simpleName}>"
                else -> answer(method.name) ?: error("unexpected call ${method.name}")
            }
        } as T

    // A channels fake whose primaryChannel fails (counting the calls) or never answers (a load that is pending).
    private fun channels(calls: AtomicInteger, pending: Boolean): ChannelsApi =
        fake { name ->
            if (name != "primaryChannel") null
            else {
                calls.incrementAndGet()
                if (pending) COROUTINE_SUSPENDED
                else ApiResult.Failure(ApiError(status = 503, code = null, message = failureMessage))
            }
        }

    @Composable
    private fun Env(content: @Composable () -> Unit) {
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

    private fun commandsController(channels: ChannelsApi): CommandsController =
        CommandsController(
            channelsApi = channels,
            commandsApi = fake<CommandsApi> { null },
            builtinsApi = fake<BuiltinsApi> { null },
            pipelinesApi = fake<PipelinesApi> { null },
            pickListsApi = fake<PickListsApi> { null },
            codeScriptsApi = null,
        )

    @Composable
    private fun CommandsContent(channels: ChannelsApi) {
        val builtins: BuiltinsApi = fake { null }
        CommandsScreen(
            controller = commandsController(channels),
            role = ManagementRole.Broadcaster,
            templateHelpersApi = fake<TemplateHelpersApi> { null },
            repliesController = BuiltinRepliesController(channels, builtins),
            detailController = BuiltinDetailController(channels, builtins),
        )
    }

    private fun timersController(channels: ChannelsApi): TimersController =
        TimersController(
            channelsApi = channels,
            timersApi = fake<TimersApi> { null },
            pipelinesApi = fake<PipelinesApi> { null },
            pickListsApi = fake<PickListsApi> { null },
            platformTemplatesApi = fake<PlatformTemplatesApi> { null },
        )

    private fun quotesApi(calls: AtomicInteger, pending: Boolean): QuotesApi =
        fake { name ->
            if (name != "page") null
            else {
                calls.incrementAndGet()
                if (pending) COROUTINE_SUSPENDED
                else ApiResult.Failure(ApiError(status = 503, code = null, message = failureMessage))
            }
        }

    @Test
    fun commands_failed_load_shows_the_failed_state_and_retry_loads_again() = runComposeUiTest {
        val calls = AtomicInteger()
        setContent { Env { CommandsContent(channels(calls, pending = false)) } }
        waitForIdle()
        onNodeWithTag(LoadFailedStateTag).assertIsDisplayed()
        onNodeWithText(failureMessage, substring = true).assertIsDisplayed()
        val before: Int = calls.get()
        onNodeWithTag(LoadFailedRetryTag).performClick()
        waitForIdle()
        assertEquals(before + 1, calls.get(), "Retry must call the load again")
        onNodeWithTag(LoadFailedStateTag).assertIsDisplayed()
    }

    @Test
    fun commands_pending_load_shows_the_skeleton() = runComposeUiTest {
        setContent { Env { CommandsContent(channels(AtomicInteger(), pending = true)) } }
        waitForIdle()
        onNodeWithTag(SkeletonLoadingTag).assertIsDisplayed()
    }

    @Test
    fun timers_failed_load_shows_the_failed_state_and_retry_loads_again() = runComposeUiTest {
        val calls = AtomicInteger()
        setContent {
            Env {
                TimersScreen(
                    controller = timersController(channels(calls, pending = false)),
                    role = ManagementRole.Broadcaster,
                    templateHelpersApi = fake<TemplateHelpersApi> { null },
                )
            }
        }
        waitForIdle()
        onNodeWithTag(LoadFailedStateTag).assertIsDisplayed()
        onNodeWithText(failureMessage, substring = true).assertIsDisplayed()
        val before: Int = calls.get()
        onNodeWithTag(LoadFailedRetryTag).performClick()
        waitForIdle()
        assertEquals(before + 1, calls.get(), "Retry must call the load again")
    }

    @Test
    fun timers_pending_load_shows_the_skeleton() = runComposeUiTest {
        setContent {
            Env {
                TimersScreen(
                    controller = timersController(channels(AtomicInteger(), pending = true)),
                    role = ManagementRole.Broadcaster,
                    templateHelpersApi = fake<TemplateHelpersApi> { null },
                )
            }
        }
        waitForIdle()
        onNodeWithTag(SkeletonLoadingTag).assertIsDisplayed()
    }

    @Test
    fun quotes_failed_load_shows_the_failed_state_and_retry_loads_again() = runComposeUiTest {
        val calls = AtomicInteger()
        setContent {
            Env {
                QuotesScreen(
                    controller = QuotesController(quotesApi(calls, pending = false)),
                    heldActionKeys = emptySet(),
                )
            }
        }
        waitForIdle()
        onNodeWithTag(LoadFailedStateTag).assertIsDisplayed()
        onNodeWithText(failureMessage, substring = true).assertIsDisplayed()
        val before: Int = calls.get()
        onNodeWithTag(LoadFailedRetryTag).performClick()
        waitForIdle()
        assertEquals(before + 1, calls.get(), "Retry must call the load again")
    }

    @Test
    fun quotes_pending_load_shows_the_skeleton() = runComposeUiTest {
        setContent {
            Env {
                QuotesScreen(
                    controller = QuotesController(quotesApi(AtomicInteger(), pending = true)),
                    heldActionKeys = emptySet(),
                )
            }
        }
        waitForIdle()
        onNodeWithTag(SkeletonLoadingTag).assertIsDisplayed()
    }

    @Test
    fun no_results_state_names_the_query_and_clear_filter_fires() = runComposeUiTest {
        val cleared = AtomicInteger()
        setContent { Env { NoResultsState(query = "zzz", onClearFilter = { cleared.incrementAndGet() }) } }
        onNodeWithText("No results for “zzz”").assertIsDisplayed()
        onNodeWithText("Clear filter").performClick()
        assertEquals(1, cleared.get())
    }
}
