// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import kotlinx.coroutines.flow.SharedFlow

/**
 * The backend announces every config mutation on the dashboard hub as `ConfigChanged(domain, entityId,
 * action)` — a command added, a timer edited, a quote deleted, by ANY operator or by the bot itself. The
 * client used to drop those as [HubEvent.Unknown], so a page kept showing whatever it fetched when it
 * opened: edits only appeared after a manual reload, and a second moderator's change never appeared at all.
 *
 * A controller subscribes with the domain it renders and refetches when its domain speaks. Refetching (as
 * opposed to patching state from the payload) is deliberate: the event carries only what changed, while the
 * list a page renders is filtered, sorted and paged server-side — reconstructing that client-side is how
 * views drift out of step with the source of truth.
 */
suspend fun SharedFlow<HubEvent>.onConfigChange(
    vararg domains: String,
    reload: suspend (change: HubConfigChanged) -> Unit,
) {
    val watched: Set<String> = domains.toSet()
    collect { event ->
        if (event is HubEvent.ConfigChanged && event.change.domain in watched) {
            reload(event.change)
        }
    }
}
