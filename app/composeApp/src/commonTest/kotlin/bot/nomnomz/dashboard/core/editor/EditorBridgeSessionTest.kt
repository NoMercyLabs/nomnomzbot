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
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

// The bridge both hosts (web iframe, desktop native web view) run: the page's messages must reach the caller's
// compile / history / test-run with the right data, and each reply must go back in the shape editor.js reads.
class EditorBridgeSessionTest {
    private fun parse(message: String): JsonObject = Json.parseToJsonElement(message).jsonObject

    private class Harness(
        history: EditorHistory? = null,
        testRun: EditorTestRun? = null,
        private val feedback: CompileFeedback = CompileFeedback(ok = true, message = "Compiled v2"),
    ) {
        val compiled: MutableList<Map<String, String>> = mutableListOf()
        val posted: MutableList<String> = mutableListOf()
        val session: EditorBridgeSession =
            EditorBridgeSession(
                title = "Alerts",
                initialFiles = mapOf("src/App.vue" to "<template/>", "src/util.ts" to "export const a = 1"),
                entryPath = "src/App.vue",
                language = "vue",
                sdkTypes = "declare const nnz: { on(e: string): void };",
                eventSubscriptions = listOf("channel.follow"),
                history = history,
                testRun = testRun,
                compile = { files ->
                    compiled += files
                    feedback
                },
                post = { message -> posted += message },
            )
    }

    @Test
    fun readyIsAnsweredWithTheFullProjectAndSdkTypes() = runTest {
        val harness = Harness()

        assertTrue(harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.READY)))

        val open: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.OPEN, open["type"]!!.jsonPrimitive.content)
        val payload: JsonObject = open["payload"]!!.jsonObject
        assertEquals("Alerts", payload["title"]!!.jsonPrimitive.content)
        assertEquals("src/App.vue", payload["entry"]!!.jsonPrimitive.content)
        assertEquals("export const a = 1", payload["files"]!!.jsonObject["src/util.ts"]!!.jsonPrimitive.content)
        assertEquals("declare const nnz: { on(e: string): void };", payload["sdkTypes"]!!.jsonPrimitive.content)
        assertEquals("channel.follow", payload["eventSubscriptions"]!!.jsonArray.single().jsonPrimitive.content)
        assertFalse(payload["testRunEnabled"]!!.jsonPrimitive.boolean)
        assertNull(payload["history"], "no history panel when the caller passed none")
        assertTrue(harness.compiled.isEmpty(), "opening never compiles")
    }

    @Test
    fun saveCompilesTheExactFilesAndPostsTheResultBack() = runTest {
        val harness = Harness(feedback = CompileFeedback(ok = false, message = "App.vue:3 unexpected token"))
        val edited: Map<String, String> = mapOf("src/App.vue" to "<template>v2</template>", "src/new.ts" to "x")

        val raw: String =
            """{"type":"nnz:editor:save","files":{"src/App.vue":"<template>v2</template>","src/new.ts":"x"}}"""
        assertTrue(harness.session.handle(EditorBridgeProtocol.decode(raw)!!))

        assertEquals(listOf(edited), harness.compiled)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.COMPILED, reply["type"]!!.jsonPrimitive.content)
        assertFalse(reply["ok"]!!.jsonPrimitive.boolean)
        assertEquals("App.vue:3 unexpected token", reply["message"]!!.jsonPrimitive.content)
    }

    @Test
    fun closeEndsTheSessionWithoutPostingOrCompiling() = runTest {
        val harness = Harness()

        assertFalse(harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.CLOSE)))

        assertTrue(harness.posted.isEmpty())
        assertTrue(harness.compiled.isEmpty())
    }

    @Test
    fun historyRollbackPassesTheVersionIdAndPostsTheRefreshedPage() = runTest {
        val rolledBack: MutableList<String> = mutableListOf()
        val page = EditorVersionsPage(listOf(EditorVersionSummary("v-7", 7, "valid", isCurrent = true)), hasMore = false)
        val history =
            EditorHistory(
                initialVersions = emptyList(),
                initialHasMore = false,
                loadMore = { EditorOutcome.Failed("unused") },
                rollback = { id ->
                    rolledBack += id
                    EditorOutcome.Ok(page)
                },
                delete = { EditorOutcome.Failed("unused") },
            )
        val harness = Harness(history = history)

        harness.session.handle(EditorBridgeProtocol.decode("""{"type":"nnz:editor:historyRollback","versionId":"v-7"}""")!!)

        assertEquals(listOf("v-7"), rolledBack)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.HISTORY_PAGE, reply["type"]!!.jsonPrimitive.content)
        val row: JsonObject = reply["payload"]!!.jsonObject["versions"]!!.jsonArray.single().jsonObject
        assertEquals("v-7", row["id"]!!.jsonPrimitive.content)
        assertTrue(row["isCurrent"]!!.jsonPrimitive.boolean)
    }

    @Test
    fun historyRequestWithoutHistoryIsAVisibleErrorNotSilence() = runTest {
        val harness = Harness()

        harness.session.handle(EditorInboundMessage(EditorBridgeProtocol.HISTORY_LOAD_MORE))

        assertEquals(EditorBridgeProtocol.HISTORY_ERROR, parse(harness.posted.single())["type"]!!.jsonPrimitive.content)
    }

    @Test
    fun testRunPassesVariablesAndArgsAndPostsCapturedEffects() = runTest {
        val received: MutableList<Pair<Map<String, String>, List<String>>> = mutableListOf()
        val testRun =
            EditorTestRun { variables, args ->
                received += variables to args
                EditorOutcome.Ok(
                    EditorTestRunResult(
                        success = true,
                        durationMs = 12,
                        hostCallCount = 1,
                        error = null,
                        chatOutput = listOf("hi Stoney_Eagle"),
                        effects = listOf(EditorTestRunEffect("chat.send", "\"hi\"")),
                    )
                )
            }
        val harness = Harness(testRun = testRun)

        harness.session.handle(
            EditorBridgeProtocol.decode("""{"type":"nnz:editor:testRun","variables":{"user":"Stoney_Eagle"},"args":["a","b"]}""")!!
        )

        assertEquals(listOf(mapOf("user" to "Stoney_Eagle") to listOf("a", "b")), received)
        val reply: JsonObject = parse(harness.posted.single())
        assertEquals(EditorBridgeProtocol.TEST_RUN_RESULT, reply["type"]!!.jsonPrimitive.content)
        assertTrue(reply["ok"]!!.jsonPrimitive.boolean)
        assertEquals("hi Stoney_Eagle", reply["chatOutput"]!!.jsonArray.single().jsonPrimitive.content)
        assertEquals("chat.send", reply["effects"]!!.jsonArray.single().jsonObject["name"]!!.jsonPrimitive.content)
    }
}
