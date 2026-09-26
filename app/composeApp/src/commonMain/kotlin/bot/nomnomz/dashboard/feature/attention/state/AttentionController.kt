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

import bot.nomnomz.dashboard.core.navigation.ShellRouteSlug
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.NotificationsApi
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** The `ConfigChanged` domain the backend pushes when a channel's action-required inbox may have changed. */
const val ATTENTION_HUB_DOMAIN: String = "notifications"

/**
 * The shell-wide action-required state (plan item A0): the active channel's items, visible on every page from
 * the shell frame. Loaded when the channel resolves and refetched ONLY when the backend says it changed
 * (`ConfigChanged("notifications")`, or an AutoMod queue change) — never polled. A burst of pushes collapses
 * into at most one follow-up fetch. A failed fetch keeps the last known list: showing stale-but-real items is
 * honest, blanking them would hide a real problem.
 */
class AttentionController(private val notificationsApi: NotificationsApi) {
    private val _items: MutableStateFlow<List<ActionRequiredItem>> = MutableStateFlow(emptyList())

    /** The active channel's current items, newest first (the backend's order). */
    val items: StateFlow<List<ActionRequiredItem>> = _items.asStateFlow()

    private var channelId: String? = null
    private val fetchLock: Mutex = Mutex()
    private var refetchRequested: Boolean = false

    /** Switch to [channel] and load its items. */
    suspend fun load(channel: String) {
        if (channelId != channel) _items.value = emptyList()
        channelId = channel
        refresh()
    }

    /** Refetch the list for the active channel; concurrent calls coalesce into one extra fetch. */
    suspend fun refresh() {
        if (fetchLock.isLocked) {
            refetchRequested = true
            return
        }
        fetchLock.withLock {
            do {
                refetchRequested = false
                fetchOnce()
            } while (refetchRequested)
        }
    }

    /** Refetch whenever the backend signals that the inbox may have changed. */
    suspend fun subscribeToHub(events: Flow<HubEvent>) {
        events.collect { event ->
            val changed: Boolean =
                when (event) {
                    is HubEvent.ConfigChanged -> event.change.domain == ATTENTION_HUB_DOMAIN
                    is HubEvent.AutoModQueueChanged -> true
                    else -> false
                }
            if (changed) refresh()
        }
    }

    private suspend fun fetchOnce() {
        val channel: String = channelId ?: return
        when (val result: ApiResult<List<ActionRequiredItem>> = notificationsApi.actionRequired(channel)) {
            is ApiResult.Ok -> if (channelId == channel) _items.value = result.value
            is ApiResult.Failure -> Unit
        }
    }
}

/**
 * The three-way severity scale an [ActionRequiredItem.severity] maps onto. Unknown future values read as
 * [Warning] — attention-worthy, not alarming. Declared in rank order, most urgent first.
 */
enum class AttentionSeverity {
    Critical,
    Warning,
    Info,
}

/** Maps the wire severity (`critical` | `warning` | `info`) to [AttentionSeverity]. */
fun attentionSeverityFor(severity: String): AttentionSeverity =
    when (severity) {
        "critical" -> AttentionSeverity.Critical
        "info" -> AttentionSeverity.Info
        else -> AttentionSeverity.Warning
    }

/** [items] grouped by severity, most urgent group first, empty groups left out; order inside a group is kept. */
fun groupBySeverity(items: List<ActionRequiredItem>): List<Pair<AttentionSeverity, List<ActionRequiredItem>>> {
    val bySeverity: Map<AttentionSeverity, List<ActionRequiredItem>> =
        items.groupBy { attentionSeverityFor(it.severity) }
    return AttentionSeverity.entries.mapNotNull { severity ->
        bySeverity[severity]?.let { group -> severity to group }
    }
}

/** The most urgent severity among [items], or null when there are none. */
fun highestSeverity(items: List<ActionRequiredItem>): AttentionSeverity? =
    items.minOfOrNull { attentionSeverityFor(it.severity) }

/**
 * The shell page an item is fixed on, from its [ActionRequiredItem.deepLinkRoute] slug. Null for a slug the
 * dashboard does not know: no navigation is honest, a wrong page is not.
 */
fun attentionRouteOf(item: ActionRequiredItem): ShellRoute? = ShellRouteSlug.find(item.deepLinkRoute)
