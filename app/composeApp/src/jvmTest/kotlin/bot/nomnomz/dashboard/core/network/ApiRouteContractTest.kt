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
 * added tomorrow is covered without anyone remembering to add it here. The scan covers every production
 * (`*Main`) source set, not just `core/network` — `OAuthLauncher.jvm.kt`, `OAuthLauncher.wasmJs.kt`, and
 * `TwitchAppCredentialsController.kt` build request/redirect URLs with a leading slash outside that
 * package, and were invisible to the original directory-scoped, no-leading-slash scan.
 */
class ApiRouteContractTest {

    /**
     * `client.getEnvelope("api/v1/channels/$channelId/trust/policy")` → `api/v1/channels/{}/trust/policy`.
     * Kotlin interpolations (`$id`, `${'$'}{x.y}`) become `{}`, matching how the spec templates its own
     * parameters, so the comparison is about the literal segments — which is exactly where typos live.
     *
     * The leading slash is optional: `core/network` clients build paths without one
     * (`"api/v1/channels/…"`), while `OAuthLauncher`/`TwitchAppCredentialsController` build full
     * request/redirect URLs with one (`"/api/v1/auth/twitch"`) — both are the same route.
     */
    private val urlLiteral: Regex = Regex(""""(/?api/v1/[^"]*)"""")
    private val interpolation: Regex = Regex("""\$\{[^}]*\}|\$[A-Za-z_][A-Za-z0-9_]*""")

    private fun normalise(raw: String): String =
        interpolation
            .replace(raw, "{}")
            .removePrefix("/")
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
     *
     * The reverse direction is NOT symmetric: a spec `{}` (a real path parameter) only matches a client
     * `{}` (a real interpolation) — never a client literal. Letting a bare spec `{}` match any client
     * literal is the hole this asymmetry closes: `…/sound-clips/stop` (a client literal, no parameter
     * involved) must not pass just because a parameterised sibling `…/sound-clips/{}` exists in the spec.
     */
    private fun matches(clientPath: String, specPath: String): Boolean {
        val a: List<String> = clientPath.split('/')
        val b: List<String> = specPath.split('/')
        if (a.size != b.size) return false
        return a.indices.all { i -> a[i] == b[i] || a[i] == "{}" }
    }

    // Empty, and it must stay that way. The one entry that lived here — POST users/{}/export — was a
    // control that 404'd on every click while reading as success; it now calls the compliance plane's
    // real export. A new entry here is a shipped dead button, not a tolerated exception.
    private val knownDeadRoutes: Set<String> = emptySet()

    /**
     * Regression for the guard hole proven live by commit 13906b7f: a client literal segment
     * (`…/sound-clips/stop`) must NOT match a spec that only declares a parameterised sibling
     * (`…/sound-clips/{}`) — the `{}` there is a real path parameter, not a wildcard for any literal.
     * The per-provider case (`…/setup/credentials/{}` client interpolation vs. `…/setup/credentials/twitch`
     * spec literal) must keep matching — that asymmetry is intentional and stays.
     */
    @Test
    fun spec_parameter_does_not_swallow_an_unrelated_client_literal() {
        val stopClient = normalise("api/v1/sound-clips/stop")
        val soundClipsParamSpec = normalise("api/v1/sound-clips/{id}")
        if (matches(stopClient, soundClipsParamSpec))
            fail(
                "\"$stopClient\" matched spec \"$soundClipsParamSpec\" — a spec parameter must not swallow " +
                    "an unrelated client literal."
            )

        val credentialsClient = normalise("api/v1/setup/credentials/\$provider")
        val credentialsSpec = normalise("api/v1/setup/credentials/twitch")
        if (!matches(credentialsClient, credentialsSpec))
            fail(
                "\"$credentialsClient\" did not match spec \"$credentialsSpec\" — a client interpolation " +
                    "must still match a per-provider spec literal."
            )
    }

    /**
     * `OAuthLauncher.jvm.kt`/`.wasmJs.kt` and `TwitchAppCredentialsController.kt` build URLs as
     * `"/api/v1/auth/twitch"` (leading slash) while `core/network` clients build `"api/v1/…"` (none) — both
     * name the same route, so normalisation must collapse them to the identical string. Regression for the
     * original regex (`"(api/v1/…)"`), which anchored on a quote immediately followed by `api`, so a
     * leading-slash literal never matched at all and those three files were silently unchecked.
     */
    @Test
    fun leading_slash_literal_normalises_the_same_as_no_leading_slash() {
        val withSlash: String = normalise("/api/v1/auth/twitch")
        val withoutSlash: String = normalise("api/v1/auth/twitch")
        if (withSlash != withoutSlash)
            fail(
                "\"/api/v1/auth/twitch\" normalised to \"$withSlash\" but \"api/v1/auth/twitch\" " +
                    "normalised to \"$withoutSlash\" — they name the same route and must match."
            )

        val matched: Boolean = urlLiteral.containsMatchIn("\"/api/v1/auth/twitch/bot\"")
        if (!matched)
            fail("urlLiteral did not match a leading-slash literal — the scan would silently skip it.")
    }

    @Test
    fun every_client_rest_url_matches_a_route_the_api_serves() {
        val specPaths: List<String> =
            Json.parseToJsonElement(specFile().readText())
                .jsonObject["paths"]!!
                .jsonObject
                .keys
                .map { path -> normalise(path.removePrefix("/")) }

        val offenders: MutableList<String> = mutableListOf()
        var checked = 0

        mainSourceDirs()
            .flatMap { dir -> dir.walkTopDown() }
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

    /**
     * Every production Kotlin source set (`commonMain`, `jvmMain`, `wasmJsMain`, …) — `*Test` source sets are
     * excluded because their fixtures build fake URLs on purpose. Route literals live outside `core/network`
     * too: `OAuthLauncher.jvm.kt`/`OAuthLauncher.wasmJs.kt` build the platform-specific authorize redirect,
     * and `TwitchAppCredentialsController.kt` builds the redirect URL shown to the user — both are real
     * requests the API must actually serve.
     */
    private fun mainSourceDirs(): List<File> =
        sourceRoot()
            .listFiles { file -> file.isDirectory && file.name.endsWith("Main") }
            ?.toList()
            ?: fail("Could not list source sets under ${sourceRoot()}")
}
