// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

import kotlinx.coroutines.flow.StateFlow

// mDNS LAN discovery (frontend.md §6) — the zero-friction onboarding seam. The bot advertises
// `_nomnomz._tcp` on the LAN (Makaretu, server-side); this is the APP's browse side. The Connect
// screen observes [discovered] and renders each found backend as a click-to-connect row, so a
// streamer on the same network never types a URL.
//
//   Desktop (jvm): jmDNS browses `_nomnomz._tcp.local.`, maps each resolved service to a
//                  [ConnectionProfile] (source = Discovered), and pushes the live set onto [discovered].
//   Web (wasmJs):  a deliberate no-op — the web build is served by ITS OWN bot (single origin), so it
//                  never browses. [discovered] stays empty; start/stop do nothing.
//
// The seam is a commonMain INTERFACE so the Connect controller depends only on the contract (and tests
// fake it without any mDNS) — the platform browse engine is supplied by the [lanDiscovery] expect factory
// (jmDNS on desktop, a no-op on web), matching the OAuthLauncher / TokenVault per-target pattern.
interface LanDiscovery {
    /**
     * Whether this platform can browse the LAN at all — true on desktop (jmDNS), false on web (single-origin, no
     * browse primitive). The Connect screen hides the whole "found on your network" section when this is false, so
     * the web build never shows a permanently-"searching" hint for something it can't do.
     */
    val isSupported: Boolean

    /** The live set of backends seen on the LAN, deduped by the advertised instance id. */
    val discovered: StateFlow<List<ConnectionProfile>>

    /** Begin browsing. Idempotent — a second call while running is a no-op. */
    fun start()

    /** Stop browsing and release the browser. The discovered set is cleared. */
    fun stop()
}

/** The per-target [LanDiscovery] implementation — jmDNS on desktop, a no-op on web. */
expect fun lanDiscovery(): LanDiscovery

// The advertised TXT record keys the bot publishes alongside `_nomnomz._tcp` (server-side Makaretu).
// Centralized here so the platform browse engine and the mapping stay in lock-step.
internal object LanServiceTxt {
    /** The bot's `_nomnomz._tcp` service type, fully-qualified for mDNS browse. */
    const val SERVICE_TYPE: String = "_nomnomz._tcp.local."

    /** Stable per-bot instance id — the dedupe key so re-announcements collapse to one row. */
    const val KEY_INSTANCE: String = "instance"

    /** URL scheme to reach the backend (`http` / `https`); defaults to http when absent. */
    const val KEY_SCHEME: String = "scheme"

    const val DEFAULT_SCHEME: String = "http"
}

/**
 * Map a resolved advertisement to a [ConnectionProfile] — the pure heart of discovery, kept free of any
 * mDNS type so it is unit-testable on every target.
 *
 *  - [instanceName] — the advertised display name (e.g. "EAGLE"), shown in the Connect row.
 *  - [host]         — the resolved host/IP to reach the backend.
 *  - [port]         — the advertised port.
 *  - [txt]          — the parsed TXT record map; `instance` gives the stable dedupe id, `scheme` the URL
 *                     scheme (default http). A blank/absent `instance` falls back to `host:port` so a bot
 *                     that omits it still dedupes by endpoint.
 *
 * Returns null only when there is nothing usable to connect to (blank host or non-positive port).
 */
internal fun resolveDiscoveredProfile(
    instanceName: String,
    host: String,
    port: Int,
    txt: Map<String, String>,
): ConnectionProfile? {
    val trimmedHost: String = host.trim()
    if (trimmedHost.isEmpty() || port <= 0) return null

    val scheme: String =
        txt[LanServiceTxt.KEY_SCHEME]?.trim()?.takeIf { it.isNotEmpty() } ?: LanServiceTxt.DEFAULT_SCHEME

    val instanceId: String =
        txt[LanServiceTxt.KEY_INSTANCE]?.trim()?.takeIf { it.isNotEmpty() } ?: "$trimmedHost:$port"

    val displayName: String = instanceName.trim().takeIf { it.isNotEmpty() } ?: trimmedHost

    return ConnectionProfile(
        id = instanceId,
        displayName = displayName,
        baseUrl = "$scheme://$trimmedHost:$port",
        source = ProfileSource.Discovered,
    )
}
