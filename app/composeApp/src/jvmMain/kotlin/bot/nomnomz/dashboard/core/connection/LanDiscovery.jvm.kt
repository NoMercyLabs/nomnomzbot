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

import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import javax.jmdns.JmDNS
import javax.jmdns.ServiceEvent
import javax.jmdns.ServiceInfo
import javax.jmdns.ServiceListener
import kotlin.concurrent.thread
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/** Desktop browse engine — jmDNS resolving `_nomnomz._tcp.local.`. */
actual fun lanDiscovery(): LanDiscovery = JmdnsLanDiscovery()

// Desktop mDNS browse (frontend.md §6) — jmDNS resolving `_nomnomz._tcp.local.`. jmDNS' create() and
// socket I/O block, so the whole browser lifecycle runs on a dedicated background thread off the UI;
// resolved services are mapped to ConnectionProfiles and pushed onto a StateFlow the Connect screen
// observes. The dance is fully defensive: a missing interface, a closed network, or a malformed
// advertisement is caught and logged, never thrown — the worst case is an empty discovered list.
class JmdnsLanDiscovery : LanDiscovery {

    // Desktop can browse the LAN, so the Connect screen shows the discovery section.
    override val isSupported: Boolean = true

    private val _discovered: MutableStateFlow<List<ConnectionProfile>> = MutableStateFlow(emptyList())
    override val discovered: StateFlow<List<ConnectionProfile>> = _discovered.asStateFlow()

    // jmDNS lives entirely on the worker thread; the lock guards the start/stop transition so a rapid
    // show/dispose can't leave two browsers or a half-open one behind. [running] is the authoritative
    // started/stopped flag — it flips synchronously in start/stop, so the async worker can detect a stop
    // that raced in before create() returned.
    private val lock: Any = Any()
    private var running: Boolean = false
    private var instances: List<JmDNS> = emptyList()
    private var listener: ServiceListener? = null

    override fun start() {
        synchronized(lock) {
            if (running) return
            running = true
            // create() does a blocking network probe; never run it on the caller's (UI) thread.
            thread(name = "nnz-lan-discovery", isDaemon = true) { runBrowser() }
        }
    }

    override fun stop() {
        val toClose: List<JmDNS>
        val active: ServiceListener?
        synchronized(lock) {
            running = false
            toClose = instances
            active = listener
            instances = emptyList()
            listener = null
        }
        active?.let { l ->
            toClose.forEach { dns ->
                runCatching { dns.removeServiceListener(LanServiceTxt.SERVICE_TYPE, l) }
            }
        }
        toClose.forEach { dns -> runCatching { dns.close() } }
        _discovered.value = emptyList()
    }

    private fun runBrowser() {
        val browseListener: ServiceListener = ProfileListener()
        val created: MutableList<JmDNS> = mutableListOf()

        // Browse on EVERY usable interface, not just InetAddress.getLocalHost(). On a machine with virtual
        // adapters (Hyper-V / WSL / Docker) getLocalHost() often resolves to a virtual NIC the bot does NOT
        // announce on, so a single-interface browser silently finds nothing. One jmDNS per real interface
        // address catches the advertisement whichever NIC it arrives on; [discovered] dedupes a bot seen on
        // several. Each create() is independently guarded — a flaky interface is skipped, never fatal.
        for (address in usableBrowseAddresses()) {
            val dns: JmDNS =
                try {
                    JmDNS.create(address)
                } catch (cause: Throwable) {
                    logFailure("create(${address.hostAddress})", cause)
                    continue
                }
            runCatching { dns.addServiceListener(LanServiceTxt.SERVICE_TYPE, browseListener) }
                .onFailure { cause -> logFailure("addServiceListener(${address.hostAddress})", cause) }
            created.add(dns)
        }

        // Last-ditch fallback: if interface enumeration yielded nothing usable, let jmDNS pick its own bind.
        if (created.isEmpty()) {
            runCatching { JmDNS.create() }
                .onSuccess { dns ->
                    runCatching { dns.addServiceListener(LanServiceTxt.SERVICE_TYPE, browseListener) }
                    created.add(dns)
                }
                .onFailure { cause -> logFailure("create(default)", cause) }
        }

        synchronized(lock) {
            // A stop() raced in before the browsers came up — tear them all down again.
            if (!running) {
                created.forEach { dns -> runCatching { dns.close() } }
                return
            }
            instances = created
            listener = browseListener
        }
    }

    // The IPv4 address of every interface that is up, multicast-capable, and not loopback — the set a bot's
    // mDNS announcement can arrive on. Defensive at every step: a flaky interface is skipped, never thrown.
    private fun usableBrowseAddresses(): List<InetAddress> {
        val interfaces: List<NetworkInterface> =
            runCatching { NetworkInterface.getNetworkInterfaces()?.toList() }.getOrNull() ?: return emptyList()
        val result: MutableList<InetAddress> = mutableListOf()
        for (iface in interfaces) {
            val usable: Boolean =
                runCatching { iface.isUp && iface.supportsMulticast() && !iface.isLoopback }.getOrDefault(false)
            if (!usable) continue
            val addresses: List<InetAddress> =
                runCatching { iface.inetAddresses?.toList() }.getOrNull() ?: continue
            for (address in addresses) {
                if (address is Inet4Address && !address.isLoopbackAddress) result.add(address)
            }
        }
        return result
    }

    // jmDNS fires serviceAdded with an unresolved stub, then serviceResolved once the SRV/TXT records
    // arrive — only the resolved event carries the host/port/TXT we map. We additionally request a
    // resolve on add so a slow responder still surfaces.
    private inner class ProfileListener : ServiceListener {
        override fun serviceAdded(event: ServiceEvent) {
            runCatching { event.dns.requestServiceInfo(event.type, event.name, RESOLVE_TIMEOUT_MS) }
        }

        override fun serviceResolved(event: ServiceEvent) {
            val profile: ConnectionProfile = mapServiceInfo(event.info) ?: return
            _discovered.update { current ->
                // Dedupe by the bot's identity (its unique mDNS service name = displayName), not its address: one bot
                // binds every interface and advertises a dozen addresses, so address-keying would list it a dozen
                // times. Keep a single entry, upgrading its address only if a later resolution carries a more-routable
                // one (so the real LAN address wins over a virtual-adapter one).
                val existing: ConnectionProfile? = current.firstOrNull { it.displayName == profile.displayName }
                when {
                    existing == null -> current + profile
                    addressRank(hostOf(profile.baseUrl)) > addressRank(hostOf(existing.baseUrl)) ->
                        current.filterNot { it.displayName == profile.displayName } + profile
                    else -> current
                }
            }
        }

        override fun serviceRemoved(event: ServiceEvent) {
            val goneName: String = mapServiceInfo(event.info)?.displayName ?: return
            _discovered.update { current -> current.filterNot { it.displayName == goneName } }
        }
    }

    private fun logFailure(stage: String, cause: Throwable) {
        // No logging facade in the app slice yet; stderr keeps the failure visible without crashing.
        System.err.println("[LanDiscovery] $stage failed: ${cause.message}")
    }

    private companion object {
        const val RESOLVE_TIMEOUT_MS: Long = 1_000L
    }
}

/**
 * Map a jmDNS [ServiceInfo] to a [ConnectionProfile] via the shared pure [resolveDiscoveredProfile].
 * The host is the first resolved address (IPv4 preferred for a clean `http://host:port`); TXT properties
 * are flattened to a plain map so the mapping stays free of any jmDNS type. Returns null when the service
 * has no usable address/port.
 */
internal fun mapServiceInfo(info: ServiceInfo): ConnectionProfile? {
    // One connectable IPv4 out of everything the bot advertises. A self-host bot binds every interface, so mDNS
    // resolves it at the real LAN address AND every virtual adapter (Docker/Hyper-V/WSL), link-local, and IPv6
    // address. Skip loopback + link-local (169.254), ignore IPv6 (the dashboard connects over http://host:port and
    // fe80 addresses are unusable), and prefer a real LAN (192.168/10) over the likely-virtual 172.x ranges.
    val host: String =
        info.inet4Addresses
            .asSequence()
            .filterNot { it.isLoopbackAddress || it.isLinkLocalAddress }
            .maxByOrNull { addressRank(it.hostAddress) }
            ?.hostAddress ?: return null

    val txt: Map<String, String> =
        info.propertyNames
            .asSequence()
            .mapNotNull { key -> info.getPropertyString(key)?.let { value -> key to value } }
            .toMap()

    return resolveDiscoveredProfile(
        instanceName = info.name,
        host = host,
        port = info.port,
        txt = txt,
    )
}

/** Rank an IPv4 as a connect target: a real home/office LAN beats the private ranges dev tooling claims. */
internal fun addressRank(ip: String?): Int =
    when {
        ip == null -> 0
        ip.startsWith("192.168.") || ip.startsWith("10.") -> 3
        ip.startsWith("172.") -> 2
        else -> 1
    }

private fun hostOf(baseUrl: String): String? = runCatching { java.net.URI(baseUrl).host }.getOrNull()
