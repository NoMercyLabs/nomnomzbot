// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.feature.integrations.state.FakeChannelsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlinx.coroutines.test.runTest

// The inventory read is a guaranteed 503 while VTS is not connected, so the page must not ask for it then.
class VtsControllerTest {

    @Test
    fun load_skips_the_inventory_call_while_vts_is_not_connected() = runTest {
        val api = FakeVtsApi(connection = VtsConnection(status = "authorized"), inventory = VtsModelInventory())
        val controller = VtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        controller.load()

        val ready: VtsUiState.Ready = assertIs<VtsUiState.Ready>(controller.state.value)
        assertNull(ready.inventory)
        assertEquals(0, api.inventoryCalls)
    }

    @Test
    fun load_reads_the_inventory_once_vts_is_connected() = runTest {
        val inventory = VtsModelInventory()
        val api = FakeVtsApi(connection = VtsConnection(status = "connected"), inventory = inventory)
        val controller = VtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        controller.load()

        val ready: VtsUiState.Ready = assertIs<VtsUiState.Ready>(controller.state.value)
        assertEquals(inventory, ready.inventory)
        assertEquals(1, api.inventoryCalls)
    }
}
