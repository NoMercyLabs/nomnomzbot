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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json

// The desktop half of the editor bridge: what the native web view is pointed at, and how messages cross into and
// out of it. The page URL must never carry anything but the bot origin and the static page path.
class EditorWebViewBridgeTest {
    @Test
    fun pageUrlIsTheBotOriginPlusTheStaticEditorPage() {
        assertEquals("http://localhost:5080/editor/index.html", EditorWebViewBridge.editorPageUrl("http://localhost:5080/"))
        assertEquals("https://dev.nomnomz.bot/editor/index.html", EditorWebViewBridge.editorPageUrl(" https://dev.nomnomz.bot "))
    }

    @Test
    fun pageUrlNeverCarriesATokenFromTheOrigin() {
        val secret = "eyJhbGciOiJIUzI1NiJ9.secret"
        val candidates: List<String> =
            listOf(
                "https://bot.example/?access_token=$secret",
                "https://bot.example/#access_token=$secret",
                "https://user:$secret@bot.example/",
                "https://bot.example/api?token=$secret",
            )

        candidates.forEach { origin ->
            val url: String? = EditorWebViewBridge.editorPageUrl(origin)
            assertEquals("https://bot.example/editor/index.html", url, "for $origin")
            assertFalse(url!!.contains(secret))
        }
    }

    @Test
    fun noUsableOriginMeansNoPage() {
        assertNull(EditorWebViewBridge.editorPageUrl(null))
        assertNull(EditorWebViewBridge.editorPageUrl(""))
        assertNull(EditorWebViewBridge.editorPageUrl("file:///C:/editor/index.html"))
        assertNull(EditorWebViewBridge.editorPageUrl("javascript:alert(1)"))
    }

    @Test
    fun initScriptForwardsExactlyThePageToHostTypes() {
        val script: String = EditorWebViewBridge.initScript()

        EditorBridgeProtocol.pageToHostTypes.forEach { type -> assertTrue(script.contains("\"$type\""), type) }
        listOf(EditorBridgeProtocol.OPEN, EditorBridgeProtocol.COMPILED, EditorBridgeProtocol.HISTORY_PAGE).forEach {
            type ->
            assertFalse(script.contains("\"$type\""), "host-to-page $type must not be forwarded back to the host")
        }
        assertTrue(script.contains("window.${EditorWebViewBridge.HOST_FUNCTION}(JSON.stringify(data))"))
        assertTrue(script.contains("event.origin !== window.location.origin"))
    }

    @Test
    fun deliverScriptPostsTheSameJsonIntoThePage() {
        val message: String = EditorBridgeProtocol.compiled(CompileFeedback(ok = true, message = "line \"1\"\n</script>\u2028"))

        val script: String = EditorWebViewBridge.deliverScript(message)

        val prefix = "window.postMessage("
        val suffix = ", window.location.origin);"
        assertTrue(script.startsWith(prefix) && script.endsWith(suffix))
        val literal: String = script.removePrefix(prefix).removeSuffix(suffix)
        assertEquals(Json.parseToJsonElement(message), Json.parseToJsonElement(literal))
    }

    // The envelope shape is the binding's own (captured live from WebView2): name, sequence, argument array.
    private fun envelope(name: String, args: String): String = """{"name":"$name","seq":1,"args":$args}"""

    @Test
    fun hostCallEnvelopeUnwrapsToTheMessageJson() {
        val json = """{"type":"nnz:editor:ready"}"""

        assertEquals(json, EditorWebViewBridge.unwrapHostCall(envelope("nnzEditorHost", Json.encodeToString(listOf(json)))))
    }

    @Test
    fun anythingButOneStringArgumentToTheEditorFunctionIsRejected() {
        val args: String = Json.encodeToString(listOf("""{"type":"nnz:editor:ready"}"""))

        assertNull(EditorWebViewBridge.unwrapHostCall(envelope("someOtherBinding", args)))
        assertNull(EditorWebViewBridge.unwrapHostCall(envelope("nnzEditorHost", "[]")))
        assertNull(EditorWebViewBridge.unwrapHostCall(envelope("nnzEditorHost", "[42]")))
        assertNull(EditorWebViewBridge.unwrapHostCall(envelope("nnzEditorHost", """["a","b"]""")))
        assertNull(EditorWebViewBridge.unwrapHostCall(args))
        assertNull(EditorWebViewBridge.unwrapHostCall("not json"))
    }

    // A save typed in the native web view travels: page JSON -> binding envelope -> unwrap -> decode -> session ->
    // caller's compile -> reply script posted back into the page.
    @Test
    fun saveRoundTripsFromTheWebViewThroughCompileAndBack() = runTest {
        val compiled: MutableList<Map<String, String>> = mutableListOf()
        val scripts: MutableList<String> = mutableListOf()
        val session =
            EditorBridgeSession(
                title = "Counter",
                initialFiles = mapOf("main.ts" to "old"),
                entryPath = "main.ts",
                language = "script",
                sdkTypes = "",
                eventSubscriptions = emptyList(),
                history = null,
                testRun = null,
                compile = { files ->
                    compiled += files
                    CompileFeedback(ok = true, message = "Published v4")
                },
                post = { message -> scripts += EditorWebViewBridge.deliverScript(message) },
            )
        val fromPage = """{"type":"nnz:editor:save","files":{"main.ts":"nnz.chat.send('hi')"}}"""

        val raw: String = EditorWebViewBridge.unwrapHostCall(envelope("nnzEditorHost", Json.encodeToString(listOf(fromPage))))!!
        assertTrue(session.handle(EditorBridgeProtocol.decode(raw)!!))

        assertEquals(listOf(mapOf("main.ts" to "nnz.chat.send('hi')")), compiled)
        assertEquals(
            """window.postMessage({"type":"nnz:editor:compiled","ok":true,"message":"Published v4"}, window.location.origin);""",
            scripts.single(),
        )
    }

    @Test
    fun webView2RuntimeVersionParsing() {
        val installed =
            "\r\nHKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226}\r\n    pv    REG_SZ    129.0.2792.65\r\n"
        val placeholder = "\r\nHKEY_LOCAL_MACHINE\\...\r\n    pv    REG_SZ    0.0.0.0\r\n"
        val empty = "\r\nHKEY_LOCAL_MACHINE\\...\r\n    pv    REG_SZ    \r\n"

        assertTrue(NativeWebViewProbe.isUsableRuntimeVersion(installed))
        assertFalse(NativeWebViewProbe.isUsableRuntimeVersion(placeholder))
        assertFalse(NativeWebViewProbe.isUsableRuntimeVersion(empty))
        assertFalse(NativeWebViewProbe.isUsableRuntimeVersion("ERROR: The system was unable to find the specified registry key"))
    }
}
