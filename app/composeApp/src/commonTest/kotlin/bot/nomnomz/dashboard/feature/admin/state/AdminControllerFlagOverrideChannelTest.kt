// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminChannel
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/**
 * A feature-flag override targets one channel. Operators know channels by login, so the override field takes a
 * login and resolves it to the id the API needs; a raw id still works for anyone who has one.
 */
class AdminControllerFlagOverrideChannelTest {

    private fun channel(id: String, login: String, displayName: String): AdminChannel =
        AdminChannel(
            id = id,
            displayName = displayName,
            login = login,
            isLive = false,
            isActive = true,
            viewerCount = 0,
            plan = "free",
            createdAt = "2026-09-01T00:00:00Z",
        )

    private fun controllerWith(vararg channels: AdminChannel): AdminController {
        val api = PagedOpsFakeAdminApi()
        api.channels = channels.toList()
        return AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())
    }

    @Test
    fun a_login_resolves_to_the_exact_channel_not_a_substring_neighbour() = runTest {
        val controller = controllerWith(
            channel("11111111-1111-1111-1111-111111111111", "qtkitten", "QtKitten"),
            channel("22222222-2222-2222-2222-222222222222", "qtkitte", "qtkitte"),
        )

        val resolved: ResolvedChannel? = controller.resolveOverrideChannel(" QTKITTE ")

        assertEquals(ResolvedChannel(id = "22222222-2222-2222-2222-222222222222", label = "qtkitte"), resolved)
    }

    @Test
    fun a_channel_id_is_used_as_is_without_a_search() = runTest {
        val controller = controllerWith()

        val resolved: ResolvedChannel? = controller.resolveOverrideChannel("33333333-3333-3333-3333-333333333333")

        assertEquals("33333333-3333-3333-3333-333333333333", resolved?.id)
    }

    @Test
    fun an_unknown_login_resolves_to_nothing() = runTest {
        val controller = controllerWith(channel("11111111-1111-1111-1111-111111111111", "qtkitten", "QtKitten"))

        assertNull(controller.resolveOverrideChannel("nobody_here"))
    }
}
