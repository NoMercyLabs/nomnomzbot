// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.requirements

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.fail

// REQUIREMENT: the dashboard must be able to REACH every endpoint the backend exposes.
//
// "Reachable" = some method in the typed network layer (core/network/*Api.kt) issues a REST call whose path
// matches the backend operation. This test scans the network sources for `"api/v1/..."` path literals, matches
// them segment-by-segment against every path in the committed OpenAPI snapshot (treating `{route params}` and
// `$interpolations` as wildcards), and DEMANDS that nothing is left unreached.
//
// Matching is at the PATH level (method-agnostic) because a source scan cannot reliably bind a literal to its
// HTTP verb — the verb lives in the client helper (getEnvelope / postUnit / deleteUnit / ...). A path that no
// literal matches is unambiguously unreachable from the dashboard; that is the gap this test exists to surface.
//
// A red result here is the backlog, not a defect in the test. When an endpoint is deliberately not a dashboard
// concern (an OBS-consumed overlay route, an inbound webhook receiver, an OAuth provider callback, the external
// automation API, a public viewer surface), it still counts against completeness on purpose — the failure list
// is the authoritative "what the dashboard does not yet expose" inventory, grouped for triage.
class EndpointReachabilityRequirementTest {

    private val networkDir: File
        get() =
            File(
                BackendContract.repoRoot(),
                "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/core/network",
            )

    /** Any double-quoted string literal that begins a v1 REST path. */
    private val pathLiteral: Regex = Regex("\"(api/v1[^\"]*)\"")

    /**
     * Every REST path the network layer calls, each split into segments with `$`-interpolated segments
     * collapsed to the `*` wildcard (they carry a runtime id — a channel/user/rule/etc.). The query string is
     * dropped: it never distinguishes one endpoint from another.
     */
    private fun frontendCallPaths(): List<List<String>> {
        val calls: MutableList<List<String>> = mutableListOf()
        networkDir.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            pathLiteral.findAll(file.readText()).forEach { match ->
                val path: String = match.groupValues[1].substringBefore('?')
                calls += path.split('/').map { segment -> if (segment.contains('$')) "*" else segment }
            }
        }
        return calls
    }

    /**
     * A backend path (`/api/v{version}/channels/{channelId}/moderation/log`) as match segments: the version
     * placeholder becomes the literal `v1`, and every `{route param}` becomes the `*` wildcard.
     */
    private fun serverSegments(openApiPath: String): List<String> =
        openApiPath.trim('/').split('/').map { raw ->
            val segment: String = raw.replace("{version}", "1")
            if (segment.contains('{')) "*" else segment
        }

    private fun matches(server: List<String>, frontend: List<String>): Boolean {
        if (server.size != frontend.size) return false
        for (index in server.indices) {
            val s: String = server[index]
            val f: String = frontend[index]
            if (s == "*" || f == "*" || s == f) continue
            return false
        }
        return true
    }

    // Guards the requirement below from silently going green if the source tree ever moves out from under the
    // scan — an empty scan must be a loud failure, never a false "everything reachable".
    @Test
    fun the_scan_found_the_network_layer_and_the_contract() {
        assertTrue(networkDir.isDirectory, "core/network source dir not found at ${networkDir.path}")
        val calls: List<List<String>> = frontendCallPaths()
        assertTrue(calls.size >= 400, "expected the network layer to hold many api/v1 path literals, found ${calls.size}")
        assertTrue(BackendContract.paths().size >= 400, "expected the full v1 path set, found ${BackendContract.paths().size}")
    }

    @Test
    fun the_dashboard_reaches_every_backend_endpoint() {
        val calls: List<List<String>> = frontendCallPaths()
        val paths: Map<String, kotlinx.serialization.json.JsonObject> = BackendContract.paths()

        val unreached: MutableList<String> = mutableListOf()
        for (path in paths.keys) {
            val server: List<String> = serverSegments(path)
            if (calls.none { matches(server, it) }) unreached += path
        }

        val total: Int = paths.size
        val reached: Int = total - unreached.size
        println("[reachability] $reached/$total backend paths reachable from core/network; ${unreached.size} unreached")

        if (unreached.isNotEmpty()) {
            val detail: String =
                unreached.sorted().joinToString("\n") { path ->
                    val verbs: String =
                        paths.getValue(path).keys
                            .filter { it in HTTP_VERBS }
                            .joinToString(",") { it.uppercase() }
                    "  • [$verbs] ${path.removePrefix("/api/v{version}")}"
                }
            fail(
                "The dashboard network layer does not reach ${unreached.size} of $total backend endpoints. " +
                    "The dashboard must be a complete reflection of the backend API — every endpoint must be " +
                    "callable from core/network. Unreached:\n$detail"
            )
        }
    }

    private companion object {
        val HTTP_VERBS: Set<String> = setOf("get", "post", "put", "delete", "patch")
    }
}
