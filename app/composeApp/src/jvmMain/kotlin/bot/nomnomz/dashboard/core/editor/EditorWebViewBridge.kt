// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

// How the served editor page talks to a native web view instead of a parent iframe — without the page knowing.
//
// In the web build the page is an iframe and posts to `window.parent`. Loaded top-level in a native web view,
// `window.parent` IS the page's own window, so those posts land back on the page itself (whose own listener
// ignores its outbound types). The init script below listens on that same window and forwards the page-to-host
// types to Kotlin over a bound native function; Kotlin answers by posting into the page the same way the web
// host does. The page stays byte-identical across both hosts, and works against any bot version that serves it.
//
// Nothing secret crosses this bridge: the page needs no token (the SDK types and project files arrive in the
// `open` message), so the page URL is just the bot origin plus the static asset path.
object EditorWebViewBridge {
    /** The native function the init script calls with each page-to-host message. */
    const val HOST_FUNCTION: String = "nnzEditorHost"

    private const val EDITOR_PAGE_PATH: String = "/editor/index.html"

    private val json: Json = Json { ignoreUnknownKeys = true }

    // scheme, then an optional userinfo part (discarded), then host[:port].
    private val originPattern: Regex = Regex("^(https?://)(?:[^/?#\\s@]*@)?([^/?#\\s@]+)")

    /**
     * The editor page on the connected bot, or null when [botOrigin] is not a plain http(s) origin. Any path,
     * query or fragment on the origin is dropped, so nothing that happened to ride along on it can leak into
     * the web view's address — including credentials in a `user:secret@` userinfo part.
     */
    fun editorPageUrl(botOrigin: String?): String? {
        val trimmed: String = botOrigin?.trim().orEmpty()
        val match: MatchResult = originPattern.find(trimmed) ?: return null
        return match.groupValues[1] + match.groupValues[2] + EDITOR_PAGE_PATH
    }

    /** Runs before the page's own scripts: forwards the page's host-bound posts to [HOST_FUNCTION]. */
    fun initScript(): String {
        val forwarded: String =
            json.encodeToString(
                JsonArray.serializer(),
                JsonArray(EditorBridgeProtocol.pageToHostTypes.sorted().map { type -> JsonPrimitive(type) }),
            )
        return """
            (function () {
                var forwarded = $forwarded;
                window.addEventListener('message', function (event) {
                    if (event.origin !== window.location.origin) { return; }
                    var data = event.data;
                    if (!data || typeof data.type !== 'string' || forwarded.indexOf(data.type) === -1) { return; }
                    if (typeof window.$HOST_FUNCTION === 'function') { window.$HOST_FUNCTION(JSON.stringify(data)); }
                });
            })();
        """
            .trimIndent()
    }

    /** Script that delivers one host-to-page [messageJson] exactly as the web host's iframe postMessage does. */
    fun deliverScript(messageJson: String): String {
        // Re-encoded through the JSON codec so the literal is always a well-formed JS expression, whatever the
        // caller built it from.
        val literal: String = json.encodeToString(JsonElement.serializer(), json.parseToJsonElement(messageJson))
        return "window.postMessage($literal, window.location.origin);"
    }

    /**
     * The message JSON the page handed [HOST_FUNCTION]. The binding delivers each call as an envelope
     * `{"name":"nnzEditorHost","seq":1,"args":["{...}"]}`; the init script passes exactly one argument, the
     * JSON-encoded message. Null for any other shape or function name.
     */
    fun unwrapHostCall(raw: String): String? {
        val envelope: JsonObject = runCatching { json.parseToJsonElement(raw) as? JsonObject }.getOrNull() ?: return null
        if ((envelope["name"] as? JsonPrimitive)?.content != HOST_FUNCTION) return null
        val argument: JsonPrimitive = (envelope["args"] as? JsonArray)?.singleOrNull() as? JsonPrimitive ?: return null
        return argument.takeIf { it.isString }?.content
    }
}
