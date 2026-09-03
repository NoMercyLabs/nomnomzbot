// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlin.test.Test
import kotlin.test.fail

/**
 * Proves every REST URL the dashboard calls is a route the API actually serves.
 *
 * [ApiContractTest] guards the SHAPE of the payloads, which left the address itself unguarded: a typo or a
 * renamed segment in a client URL compiles, type-checks, passes every DTO test, and then 404s at runtime on
 * the one screen that uses it. That gap was found by hand-diffing the S-OWN23 client URLs against the
 * regenerated snapshot; this test is that diff made permanent.
 *
 * STRUCTURAL by design — it scans the client sources for URL literals rather than listing them, so a route
 * added tomorrow is covered without anyone remembering to add it here.
 */
class ApiRouteContractTest {

    /**
     * `client.getEnvelope("api/v1/channels/$channelId/trust/policy")` → `api/v1/channels/{}/trust/policy`.
     * Kotlin interpolations (`$id`, `${'$'}{x.y}`) become `{}`, matching how the spec templates its own
     * parameters, so the comparison is about the literal segments — which is exactly where typos live.
     */
    private val urlLiteral: Regex = Regex(""""(api/v1/[^"]*)"""")
    private val interpolation: Regex = Regex("""\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*""")

    private fun normalise(raw: String): String =
        interpolation
            .replace(raw, "{}")
            // The spec templates the version segment; the client hardcodes v1.
            .replaceFirst("api/v1/", "api/v{}/")
            .let { path -> Regex("""\{[^}]*}""").replace(path, "{}") }
            .trimEnd('/')
            .let(::dropTrailingQuery)

    /**
     * Several clients append a query string by interpolation (`"…/refresh${'$'}{clientQuery()}"`,
     * `"…/tenants${'$'}query"`). That trailing placeholder is NOT a path segment, and routing ignores it.
     * The tell is the character before it: a real path parameter is preceded by `/`
     * (`…/permits/{}`), an appended query is glued straight onto the previous segment (`…/refresh{}`).
     * Only the glued form is dropped, so a genuinely missing final segment still fails.
     */
    private fun dropTrailingQuery(path: String): String =
        if (path.endsWith("{}") && path.length > 2 && path[path.length - 3] != '/')
            path.dropLast(2)
        else path

    /**
     * A client placeholder matches a spec parameter OR a literal segment: several endpoints are declared
     * per-provider on the server (`…/setup/credentials/twitch`, `…/spotify`, …) while the client builds the
     * one URL by interpolating the provider. Segment COUNT and every literal segment must still agree, so a
     * wrong or missing segment fails.
     */
    private fun matches(clientPath: String, specPath: String): Boolean {
        val a: List<String> = clientPath.split('/')
        val b: List<String> = specPath.split('/')
        if (a.size != b.size) return false
        return a.indices.all { i -> a[i] == b[i] || a[i] == "{}" || b[i] == "{}" }
    }

    /**
     * The ONE break this scan found on its first run, kept visible rather than silently tolerated.
     * `CommunityScreen`'s "export user data" control calls `POST api/v1/users/{id}/export`, which the API
     * does not serve — the only export routes are `/gdpr/export`, `/bundles/export` and
     * `/event-store/…/export`. So that button 404s today. Filed as S-DEAD-USER-EXPORT; deleting this entry
     * is part of closing it. Nothing else may be added here without the same treatment: a named reason and
     * a tracker entry.
     */
    private val knownDeadRoutes: Set<String> = setOf("api/v{}/users/{}/export")

    @Test
    fun every_client_rest_url_matches_a_route_the_api_serves() {
        val specPaths: List<String> =
            Json.parseToJsonElement(specFile().readText())
                .jsonObject["paths"]!!
                .jsonObject
                .keys
                .map { path -> normalise(path.removePrefix("/")) }

        val networkDir = File(sourceRoot(), "commonMain/kotlin/bot/nomnomz/dashboard/core/network")
        val offenders: MutableList<String> = mutableListOf()
        var checked = 0

        networkDir
            .walkTopDown()
            .filter { file -> file.isFile && file.extension == "kt" }
            .forEach { file ->
                urlLiteral.findAll(file.readText()).forEach { match ->
                    val raw: String = match.groupValues[1]
                    // Query strings are not part of the routing table.
                    val path: String = normalise(raw.substringBefore('?'))
                    checked++
                    if (path !in knownDeadRoutes && specPaths.none { spec -> matches(path, spec) })
                        offenders.add("${file.name}: \"$raw\" → $path")
                }
            }

        if (checked == 0)
            fail("Scanned no client URLs — the scan is broken, which would make this test vacuously green.")

        if (offenders.isNotEmpty())
            fail(
                "These dashboard URLs match no route in server/openapi/v1.json, so they would 404 at " +
                    "runtime. Fix the client path, or regenerate the snapshot if the API really did change " +
                    "(scripts/dev-api.ps1 start → GET /openapi/v1.json → stop):\n" +
                    offenders.sorted().joinToString("\n")
            )
    }

    /** Walk up to the committed spec, so the test is location-independent (same shape as ApiContractTest). */
    private fun specFile(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "server/openapi/v1.json")
            if (candidate.exists()) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate server/openapi/v1.json from ${System.getProperty("user.dir")}")
    }

    private fun sourceRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src")
            if (candidate.exists()) return candidate
            if (File(dir, "composeApp/src").exists()) return File(dir, "composeApp/src")
            dir = dir.parentFile
        }
        fail("Could not locate app/composeApp/src from ${System.getProperty("user.dir")}")
    }
}
