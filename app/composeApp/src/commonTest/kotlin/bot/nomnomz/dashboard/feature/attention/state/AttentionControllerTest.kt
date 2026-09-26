// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.state

import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.NotificationsApi
import bot.nomnomz.dashboard.core.realtime.HubConfigChanged
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class AttentionControllerTest {
    private fun item(id: String, severity: String, route: String = "webhooks"): ActionRequiredItem =
        ActionRequiredItem(
            id = id,
            kind = "k",
            severity = severity,
            titleKey = "attention_unknown_title",
            messageKey = "",
            deepLinkRoute = route,
        )

    @Test
    fun a_notifications_push_refetches_and_any_other_config_domain_does_not() = runTest {
        val api = FakeNotificationsApi(ApiResult.Ok(listOf(item("a", "warning"))))
        val controller = AttentionController(api)
        controller.load("ch1")
        assertEquals(listOf("a"), controller.items.value.map { it.id })

        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 8)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        api.result = ApiResult.Ok(listOf(item("a", "warning"), item("b", "critical")))
        events.emit(HubEvent.ConfigChanged(HubConfigChanged(broadcasterId = "ch1", domain = "commands")))
        assertEquals(1, api.calls, "a commands change is not an inbox change")

        events.emit(HubEvent.ConfigChanged(HubConfigChanged(broadcasterId = "ch1", domain = ATTENTION_HUB_DOMAIN)))
        assertEquals(2, api.calls)
        assertEquals(listOf("a", "b"), controller.items.value.map { it.id })
    }

    @Test
    fun a_failed_refetch_keeps_the_last_real_items_and_a_channel_switch_clears_them() = runTest {
        val api = FakeNotificationsApi(ApiResult.Ok(listOf(item("a", "critical"))))
        val controller = AttentionController(api)
        controller.load("ch1")

        api.result = ApiResult.Failure(ApiError(status = 500, code = null, message = "boom"))
        controller.refresh()
        assertEquals(listOf("a"), controller.items.value.map { it.id })

        controller.load("ch2")
        assertEquals(emptyList(), controller.items.value)
        assertEquals("ch2", api.lastChannel)
    }

    @Test
    fun items_group_by_severity_most_urgent_first_and_route_by_their_deep_link() {
        val items: List<ActionRequiredItem> =
            listOf(item("i", "info"), item("c1", "critical"), item("w", "warning"), item("c2", "critical"))

        val grouped = groupBySeverity(items)

        assertEquals(
            listOf(AttentionSeverity.Critical, AttentionSeverity.Warning, AttentionSeverity.Info),
            grouped.map { it.first },
        )
        assertEquals(listOf("c1", "c2"), grouped.first().second.map { it.id })
        assertEquals(AttentionSeverity.Critical, highestSeverity(items))
        assertNull(highestSeverity(emptyList()))
        assertEquals(ShellRoute.ModerationQueue, attentionRouteOf(item("h", "warning", "moderationqueue")))
        assertEquals(ShellRoute.SongRequests, attentionRouteOf(item("s", "info", "songrequests")))
        assertNull(attentionRouteOf(item("x", "info", "no-such-page")))
    }
}

private class FakeNotificationsApi(var result: ApiResult<List<ActionRequiredItem>>) : NotificationsApi {
    var calls: Int = 0
    var lastChannel: String? = null

    override suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>> {
        calls++
        lastChannel = channelId
        return result
    }

    override suspend fun dismissActionRequired(channelId: String, ids: List<String>): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}
