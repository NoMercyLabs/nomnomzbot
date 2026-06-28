// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(kotlin.js.ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.navigation

import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlinx.browser.window
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.map
import org.w3c.dom.events.Event

// Web: the selected page lives in the URL fragment as `#/<slug>` (a fragment, so it never round-trips to the
// server and works on the bot's single static-served origin without a routing rule). A reload re-reads it; a
// change rewrites it via `pushState`, so each page becomes a Back/Forward history entry; `popstate` feeds those
// hardware-button moves back into the shell through [externalChanges].
//
// The fragment is shared with the post-OAuth login arm (OAuthLauncher.readReturnedSession reads `#access_token=…`),
// but that runs once on boot and strips the fragment BEFORE the shell mounts, so by the time this store writes a
// route hash the token fragment is long gone — no collision.
private const val HASH_PREFIX: String = "#/"

actual class RouteStore actual constructor() {

    actual fun initialRoute(): ShellRoute = ShellRouteSlug.parse(currentSlug())

    actual fun save(route: ShellRoute) {
        val target: String = HASH_PREFIX + ShellRouteSlug.of(route)
        // Only push when the slug actually changed, so re-selecting the current page doesn't pile up duplicate
        // history entries (which would make one Back press appear to do nothing).
        if (window.location.hash != target) {
            window.history.pushState(null, "", target)
        }
    }

    // Push `#/` as the connect/unauthenticated entry BEFORE the shell writes its first page route, so the
    // browser's Back button from inside the shell lands here and the app can respond by signing out.
    actual fun pushConnectEntry() {
        window.history.pushState(null, "", HASH_PREFIX)
    }

    // One shared callbackFlow that re-emits the raw slug on every popstate — both [externalChanges] and
    // [disconnectRequests] are derived from it. The slug is empty when the hash is bare `#/`.
    private val popstateFlow: Flow<String> = callbackFlow {
        // Kotlin/Wasm's DOM `EventListener` is a JS-interop interface with no constructor, so the listener is a
        // plain `(Event) -> Unit` lambda held in a val — the SAME reference both subscribes and unsubscribes.
        val listener: (Event) -> Unit = { trySend(currentSlug()) }
        window.addEventListener("popstate", listener)
        awaitClose { window.removeEventListener("popstate", listener) }
    }

    // Shell routes: popstate events whose slug is non-empty (i.e. `#/<slug>`, not the bare `#/` entry).
    actual val externalChanges: Flow<ShellRoute> =
        popstateFlow.filter { it.isNotEmpty() }.map { ShellRouteSlug.parse(it) }

    // Disconnect signal: popstate lands on the bare `#/` entry pushed by [pushConnectEntry], meaning the
    // operator pressed Back from the first shell page — sign them out.
    actual val disconnectRequests: Flow<Unit> =
        popstateFlow.filter { it.isEmpty() }.map { Unit }

    /** The slug portion of `#/<slug>` (empty when there is no route hash, e.g. the initial load). */
    private fun currentSlug(): String = window.location.hash.removePrefix(HASH_PREFIX)
}
