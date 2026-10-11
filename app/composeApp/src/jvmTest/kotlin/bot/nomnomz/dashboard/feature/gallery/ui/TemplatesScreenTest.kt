// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.gallery.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BundlesApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.InstalledBundle
import bot.nomnomz.dashboard.core.network.MarketplaceApi
import bot.nomnomz.dashboard.core.network.MarketplaceItem
import bot.nomnomz.dashboard.feature.bundles.state.BundlesController
import bot.nomnomz.dashboard.feature.economy.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import java.lang.reflect.Proxy
import kotlin.test.Test
import kotlin.test.assertEquals

// The Templates tab of the Gallery browses the hosted catalogue through the shared BundlesController and posts the
// install for the clicked item under the chosen conflict policy.
@OptIn(ExperimentalTestApi::class)
class TemplatesScreenTest {
    private val item: MarketplaceItem =
        MarketplaceItem(
            itemId = "item-7",
            name = "Hype train pack",
            author = "pixelfox",
            summary = "Alerts for every hype level.",
        )

    @Test
    fun the_templates_tab_lists_the_catalogue_items_of_the_active_channel() = runComposeUiTest {
        val api: ScriptedMarketplace = ScriptedMarketplace(listOf(item))
        setContent { Screen(api) }
        waitForIdle()

        onNodeWithText("Hype train pack").assertExists()
        onNodeWithText("by pixelfox").assertExists()
        onNodeWithText("Alerts for every hype level.").assertExists()
        assertEquals(listOf("ch1"), api.browsedChannels)
    }

    @Test
    fun install_posts_the_clicked_item_with_the_default_rename_policy() = runComposeUiTest {
        val api: ScriptedMarketplace = ScriptedMarketplace(listOf(item))
        setContent { Screen(api) }
        waitForIdle()

        onNodeWithText("Install").performClick()
        waitForIdle()

        assertEquals(listOf(Triple("ch1", "item-7", "rename")), api.installs)
    }

    @Composable
    private fun Screen(api: ScriptedMarketplace) {
        val controller: BundlesController =
            BundlesController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                bundlesApi = noInstalledBundles(),
                marketplaceApi = api.proxy,
                commandsApi = stub(),
                pipelinesApi = stub(),
                widgetsApi = stub(),
                soundApi = stub(),
                fileBridge = stub(),
            )
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
            AppEnvironment(tag = "en") {
                NomNomzTheme { TemplatesScreen(controller = controller, role = ManagementRole.Broadcaster) }
            }
        }
    }
}

// A catalogue that answers the browse with its items and records every install it is asked for.
private class ScriptedMarketplace(private val items: List<MarketplaceItem>) {
    val browsedChannels: MutableList<String> = mutableListOf()
    val installs: MutableList<Triple<String, String, String>> = mutableListOf()

    val proxy: MarketplaceApi =
        Proxy.newProxyInstance(MarketplaceApi::class.java.classLoader, arrayOf(MarketplaceApi::class.java)) { _, method, args ->
            when (method.name) {
                "items" -> {
                    browsedChannels += args[0] as String
                    ApiResult.Ok(items)
                }
                "install" -> {
                    installs += Triple(args[0] as String, args[1] as String, args[2] as String)
                    ApiResult.Ok(InstalledBundle(id = "b1", name = "Hype train pack", version = "1.0.0"))
                }
                else -> ApiResult.Failure(ApiError(status = 500, code = "STUB", message = "not scripted"))
            }
        } as MarketplaceApi
}

// The page load reads the installed list first; a channel with nothing installed answers an empty list.
private fun noInstalledBundles(): BundlesApi =
    Proxy.newProxyInstance(BundlesApi::class.java.classLoader, arrayOf(BundlesApi::class.java)) { _, method, _ ->
        if (method.name == "installed") ApiResult.Ok(emptyList<InstalledBundle>())
        else ApiResult.Failure(ApiError(status = 500, code = "STUB", message = "not scripted"))
    } as BundlesApi

// A collaborator the load path tolerates failing (exportables, installed list): every call answers Failure.
private inline fun <reified T : Any> stub(): T =
    Proxy.newProxyInstance(T::class.java.classLoader, arrayOf(T::class.java)) { _, _, _ ->
        ApiResult.Failure(ApiError(status = 500, code = "STUB", message = "not scripted"))
    } as T
